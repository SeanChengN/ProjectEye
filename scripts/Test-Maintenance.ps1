#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Candidate)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Candidate=(Resolve-Path -LiteralPath $Candidate).Path
$release=Get-Content -LiteralPath (Join-Path $Candidate 'result.json') -Raw -Encoding utf8|ConvertFrom-Json
if(!$release.success){throw 'Successful candidate required'}
$run=Join-Path $repo ('artifacts/maintenance-tests/'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$checks=[Collections.Generic.List[string]]::new()
function Assert($Condition,$Name){if(!$Condition){throw "FAILED: $Name"};$checks.Add($Name);Write-Output "PASS: $Name"}
function Json($Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),$utf8)}
$source=Join-Path $run 'source'
Copy-Item -LiteralPath $release.payload -Destination $source -Recurse
$build=Get-Content -LiteralPath (Join-Path $release.buildDirectory 'result.json') -Raw -Encoding utf8|ConvertFrom-Json
$compiler=Join-Path $run 'compiler';New-Item -ItemType Directory -Path $compiler|Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'packaging/Maintenance') -File|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $compiler}
Copy-Item -LiteralPath (Join-Path $Candidate 'maintenance-source/BuildIdentity.cs') -Destination $compiler
& $build.msbuild (Join-Path $compiler 'Maintenance.csproj') /t:Build /nologo /v:minimal /p:DefineConstants=MAINTENANCE_TESTS "/p:EvidencePath=$compiler" "/p:TargetFrameworkRootPath=$($build.referenceRoot)" "/p:IdentitySource=$compiler/BuildIdentity.cs" "/p:ProductIcon=$repo/src/Local/ProjectEye/Resources/sunglasses.ico" > (Join-Path $run 'compile.log')
if($LASTEXITCODE-ne0){throw 'Test maintenance compilation failed'}
Copy-Item -LiteralPath (Join-Path $compiler 'bin/ProjectEye.Maintenance.exe') -Destination (Join-Path $source 'ProjectEye.Maintenance.exe') -Force
$manifest=Get-Content -LiteralPath (Join-Path $source 'payload-manifest.json') -Raw -Encoding utf8|ConvertFrom-Json
$item=$manifest|Where-Object path -eq 'ProjectEye.Maintenance.exe';$item.sha256=(Get-FileHash (Join-Path $source $item.path)).Hash;$item.bytes=(Get-Item (Join-Path $source $item.path)).Length
Json (Join-Path $source 'payload-manifest.json') $manifest
$tool=Join-Path $source 'ProjectEye.Maintenance.exe'
$counter=0
function Invoke-Tool([string[]]$Arguments,[switch]$Failure,[string]$Executable=$tool,[switch]$Handoff){
    $script:counter++;$result=Join-Path $run ("result-$script:counter.json")
    $all=@($Arguments)+@('--quiet','--result',$result)
    $quoted=@($all|ForEach-Object{'"'+($_ -replace '(\\+)$','$1$1')+'"'}) -join ' '
    $process=Start-Process -FilePath $Executable -ArgumentList $quoted -WindowStyle Hidden -PassThru -Wait
    if($Handoff){$deadline=[DateTime]::UtcNow.AddSeconds(25);while(!(Test-Path -LiteralPath $result)-and[DateTime]::UtcNow-lt$deadline){Start-Sleep -Milliseconds 100}}
    $data=if(Test-Path -LiteralPath $result){Get-Content -LiteralPath $result -Raw -Encoding utf8|ConvertFrom-Json}else{throw "Missing result $result"}
    if($Failure){if($data.success){throw 'Expected refusal'}}elseif(!$data.success){throw "$($data.message) ($result)"}
    return $data
}
$keyRoot='Software\ProjectEyeEnhanced-MaintenanceTests\Uninstall\'
$stable=$keyRoot+'ProjectEyeEnhanced-Test'
function ViewKey($View,$Name,[switch]$Write){[Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser',$View).OpenSubKey($Name,[bool]$Write)}
foreach($view in @('Registry32','Registry64')){ $k=ViewKey $view $stable;if($k){$k.Dispose();throw 'Previous test registration exists; inspect it before rerun'}}
try{
    Invoke-Tool @('--check')|Out-Null;Assert $true 'Trusted payload self-check'
    $manifestFile=Join-Path $source 'payload-manifest.json';$manifestBytes=[IO.File]::ReadAllBytes($manifestFile)
    $escaped=Get-Content -LiteralPath $manifestFile -Raw -Encoding utf8|ConvertFrom-Json;$escaped[0].path='..\outside.txt';Json $manifestFile $escaped
    try{Invoke-Tool @('--install','--source',$source,'--target',(Join-Path $run 'escape')) -Failure|Out-Null}finally{[IO.File]::WriteAllBytes($manifestFile,$manifestBytes)}
    Assert $true 'Manifest path traversal rejected'
    foreach($target in @('C:\',"$env:windir\ProjectEye-test",$env:LOCALAPPDATA)){
        Invoke-Tool @('--install','--source',$source,'--target',$target) -Failure|Out-Null
    };Assert $true 'Root, Windows and LocalAppData root rejected'
    $fake=Join-Path $run 'forged';New-Item -ItemType Directory -Path $fake|Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'test-package.json') -Destination $fake
    Invoke-Tool @('--install','--source',$source,'--target',$fake) -Failure|Out-Null;Assert $true 'Forged product marker rejected'
    $root=Join-Path $run 'installed'
    $first=Invoke-Tool @('--install','--source',$source,'--target',$root)
    Assert (Test-Path (Join-Path $first.recovery 'transaction.json')) 'New install journal retained'
    $k=ViewKey Registry64 $stable;$cmd=$k.GetValue('UninstallString');$quiet=$k.GetValue('QuietUninstallString');$k.Dispose()
    Assert ($cmd.StartsWith('"'+(Join-Path $root 'ProjectEye.Maintenance.exe')+'"')-and$quiet.EndsWith('--quiet')-and$cmd-notmatch'(?i)powershell|cmd.exe|wscript') 'Direct own EXE uninstall registrations'
    Invoke-Tool @('--rollback','--transaction',$first.recovery)|Out-Null
    Assert (!(Test-Path (Join-Path $root 'ProjectEye.exe'))) 'New install explicit rollback'
    $legacy=Join-Path $repo 'artifacts/test-release/20261116-205717-242-1.0.0-beta.2/ProjectEye-Enhanced-1.0.0-beta.2'
    Copy-Item -Path (Join-Path $legacy '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data'),(Join-Path $root 'Log')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options>test-config</Options>',$utf8)
    [IO.File]::WriteAllBytes((Join-Path $root 'Data/statistics.db'),[byte[]](0..255))
    [IO.File]::WriteAllText((Join-Path $root 'Log/test.log'),'retained log',$utf8)
    [IO.File]::WriteAllText((Join-Path $root 'my-notes.txt'),'unknown user file',$utf8)
    $dataHashes=@(Get-ChildItem (Join-Path $root 'Data') -File|Get-FileHash|Select-Object -ExpandProperty Hash)
    foreach($view in @('Registry32','Registry64')){$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser',$view);$key=$base.CreateSubKey($keyRoot+'ProjectEyeEnhanced-beta2');$key.SetValue('InstallLocation',$root);$key.SetValue('UninstallString','NEVER EXECUTE THIS OLD HOST');$key.Dispose();$base.Dispose()}
    $failedUpgrade=Invoke-Tool @('--install','--source',$source,'--target',$root,'--fail-at','registration') -Failure
    Assert ((Get-Content (Join-Path $root 'test-package.json') -Raw|ConvertFrom-Json).version-eq'1.0.0-beta.2') 'Failed legacy upgrade restores old payload'
    $restored=ViewKey Registry64 ($keyRoot+'ProjectEyeEnhanced-beta2');Assert ($restored.GetValue('UninstallString')-eq'NEVER EXECUTE THIS OLD HOST') 'Failed legacy upgrade restores registration';$restored.Dispose()
    $upgrade=Invoke-Tool @('--install','--source',$source,'--target',$root)
    Assert (((Get-Content (Join-Path $root 'test-package.json') -Raw|ConvertFrom-Json).version)-eq'1.0.0-beta.3') 'Beta.2 in-place upgrade'
    Assert (!(Test-Path (Join-Path $root 'Remove-TestVersion.ps1'))) 'Legacy PS1 removed from installation'
    foreach($view in @('Registry32','Registry64')){$key=ViewKey $view ($keyRoot+'ProjectEyeEnhanced-beta2');Assert ($null-eq$key) "Old same-path registration migrated ($view)"}
    Assert ((@(Get-ChildItem (Join-Path $root 'Data') -File|Get-FileHash|Select-Object -ExpandProperty Hash)-join',')-eq($dataHashes-join',')) 'Config and statistics unchanged after upgrade'
    Invoke-Tool @('--install','--source',$source,'--target',$root)|Out-Null;Assert $true 'Same-version repair with independent backup'
    foreach($failure in @('copy','registration')){
        $failed=Invoke-Tool @('--install','--source',$source,'--target',$root,'--fail-at',$failure) -Failure
        Assert ((Get-Content (Join-Path $failed.recovery 'transaction.json') -Raw|ConvertFrom-Json).State-eq'RolledBack') "Automatic rollback after $failure failure"
        Invoke-Tool @('--check') -Executable (Join-Path $root 'ProjectEye.Maintenance.exe')|Out-Null
    }
    $mutex=[Threading.Mutex]::new($true,'projecteye-enhanced')
    try{Invoke-Tool @('--uninstall') -Executable (Join-Path $root 'ProjectEye.Maintenance.exe') -Failure|Out-Null}finally{$mutex.ReleaseMutex();$mutex.Dispose()}
    Assert $true 'Running application mutex blocks maintenance'
    $exe=Join-Path $root 'ProjectEye.exe';$original=[IO.File]::ReadAllBytes($exe);[IO.File]::WriteAllText($exe,'modified',$utf8)
    try{Invoke-Tool @('--uninstall') -Executable (Join-Path $root 'ProjectEye.Maintenance.exe') -Failure|Out-Null}finally{[IO.File]::WriteAllBytes($exe,$original)}
    Assert $true 'Modified payload blocks uninstall'
    $junction=Join-Path $root 'external-link';New-Item -ItemType Junction -Path $junction -Target $fake|Out-Null
    Invoke-Tool @('--install','--source',$source,'--target',$root) -Failure|Out-Null;Assert $true 'Reparse subtree blocks upgrade before mutation'
    # Move the junction itself, never recursively enumerate or remove its target.
    $recycle=Join-Path $repo ('.recycle-bin/maintenance-tests/'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $recycle|Out-Null
    Move-Item -LiteralPath $junction -Destination (Join-Path $recycle 'external-link')
    $removed=Invoke-Tool @('--uninstall') -Executable (Join-Path $root 'ProjectEye.Maintenance.exe') -Handoff
    Assert (!(Test-Path (Join-Path $root 'ProjectEye.Maintenance.exe'))-and!(Test-Path (Join-Path $root 'ProjectEye.exe'))) 'Own helper completes self-removal'
    Assert (Test-Path (Join-Path $root 'my-notes.txt')) 'Unknown user file preserved on uninstall'
    Assert ((@(Get-ChildItem (Join-Path $removed.recovery 'Data') -File|Get-FileHash|Select-Object -ExpandProperty Hash)-join',')-eq($dataHashes-join',')) 'Uninstall retains config and statistics by hash'
    Assert (Test-Path (Join-Path $removed.recovery 'Log/test.log')) 'Uninstall retains logs outside application'
    $key=ViewKey Registry64 $stable;Assert ($null-eq$key) 'Only own test registration removed'
    Json (Join-Path $run 'result.json') @{success=$true;checks=$checks;candidate=$Candidate;scope='Compiled identical engine with isolated registry/shortcut constants; production installer verified separately'}
}finally{Write-Output "MAINTENANCE_EVIDENCE=$run"}
