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
function Invoke-Tool([string[]]$Arguments,[switch]$Failure,[string]$Executable=$tool,[switch]$Interrupted){
    $script:counter++;$result=Join-Path $run ("result-$script:counter.json")
    $all=@($Arguments)+@('--quiet','--result',$result)
    $quoted=@($all|ForEach-Object{'"'+($_ -replace '(\\+)$','$1$1')+'"'}) -join ' '
    $process=Start-Process -FilePath $Executable -ArgumentList $quoted -WindowStyle Hidden -PassThru -Wait
    if($Interrupted){Assert ($process.ExitCode-eq87) 'Process interruption injected';return}
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
    $recovery=Join-Path $env:LOCALAPPDATA 'ProjectEyeEnhanced-Recovery'
    function HistoryNames {if(Test-Path -LiteralPath $recovery){@(Get-ChildItem -LiteralPath $recovery -Directory|Select-Object -ExpandProperty Name|Sort-Object)}else{@()}}
    $historyBefore=@(HistoryNames)
    Invoke-Tool @('--install','--source',$source,'--target',$root)|Out-Null
    Assert (!(Test-Path (Join-Path $root '.projecteye-install-state.json'))) 'New install finishes without persistent state'
    Assert ((@(HistoryNames)-join',')-eq($historyBefore-join',')) 'New install creates no recovery directory'
    $k=ViewKey Registry64 $stable;$cmd=$k.GetValue('UninstallString');$quiet=$k.GetValue('QuietUninstallString');$k.Dispose()
    Assert ($cmd.StartsWith('"'+(Join-Path $root 'ProjectEye.Maintenance.exe')+'"')-and$quiet.EndsWith('--quiet')-and$cmd-notmatch'(?i)powershell|cmd.exe|wscript') 'Direct own EXE uninstall registrations'
    Invoke-Tool @('--rollback','--transaction',$run) -Failure|Out-Null;Assert $true 'Rollback entry removed'
    Invoke-Tool @('--remove','--target',$root)|Out-Null
    New-Item -ItemType Directory -Path $root|Out-Null
    $legacy=Join-Path $repo 'artifacts/test-release/20261116-205717-242-1.0.0-beta.2/ProjectEye-Enhanced-1.0.0-beta.2'
    Copy-Item -Path (Join-Path $legacy '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data'),(Join-Path $root 'Log')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options>test-config</Options>',$utf8)
    [IO.File]::WriteAllBytes((Join-Path $root 'Data/statistics.db'),[byte[]](0..255))
    [IO.File]::WriteAllText((Join-Path $root 'Log/test.log'),'retained log',$utf8)
    [IO.File]::WriteAllText((Join-Path $root 'my-notes.txt'),'unknown user file',$utf8)
    $dataHashes=@(Get-ChildItem (Join-Path $root 'Data') -File|Get-FileHash|Select-Object -ExpandProperty Hash)
    foreach($view in @('Registry32','Registry64')){$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser',$view);$key=$base.CreateSubKey($keyRoot+'ProjectEyeEnhanced-beta2');$key.SetValue('InstallLocation',$root);$key.SetValue('UninstallString','NEVER EXECUTE THIS OLD HOST');$key.Dispose();$base.Dispose()}
    $oldHash=(Get-FileHash (Join-Path $root 'ProjectEye.exe')).Hash
    Invoke-Tool @('--install','--source',$source,'--target',$root,'--fail-at','copy') -Failure|Out-Null
    Assert ((Get-FileHash (Join-Path $root 'ProjectEye.exe')).Hash-eq$oldHash) 'Preparation failure occurs before replacement'
    Assert (Test-Path (Join-Path $root '.projecteye-install-state.json')) 'Failure retains metadata only'
    Invoke-Tool @('--install','--source',$source,'--target',$root)|Out-Null
    Assert (((Get-Content (Join-Path $root 'test-package.json') -Raw|ConvertFrom-Json).version)-eq$release.version) 'Legacy upgrade repaired by same package'
    Assert (!(Test-Path (Join-Path $root 'Remove-TestVersion.ps1'))) 'Trusted legacy script retired'
    foreach($view in @('Registry32','Registry64')){$key=ViewKey $view ($keyRoot+'ProjectEyeEnhanced-beta2');Assert ($null-eq$key) "Old same-path registration migrated ($view)"}
    foreach($failure in @('replace','registration')){
        Invoke-Tool @('--install','--source',$source,'--target',$root,'--fail-at',$failure) -Failure|Out-Null
        Assert (Test-Path (Join-Path $root '.projecteye-install-state.json')) "No-backup $failure failure retains retry record"
        Invoke-Tool @('--install','--source',$source,'--target',$root)|Out-Null
        Invoke-Tool @('--check') -Executable (Join-Path $root 'ProjectEye.Maintenance.exe')|Out-Null
        Assert (!(Test-Path (Join-Path $root '.projecteye-install-state.json'))) "Same-package repair completes after $failure"
    }
    Invoke-Tool @('--install','--source',$source,'--target',$root,'--fail-at','interrupt') -Interrupted
    $pending=Get-Content (Join-Path $root '.projecteye-install-state.json') -Raw -Encoding utf8|ConvertFrom-Json
    Assert (!(Test-Path (Join-Path $root $pending.StageName 'Data'))) 'Interrupted stage contains no copied user data'
    $partial=Get-ChildItem -LiteralPath (Join-Path $root $pending.StageName) -File -Recurse|Select-Object -First 1
    $stream=[IO.File]::Open($partial.FullName,'Open','Write','None');try{$stream.SetLength([Math]::Min(100,$stream.Length))}finally{$stream.Dispose()}
    Assert $true 'Partial staged copy simulates mid-write interruption'

    # A forged path in an interrupted record must not widen accepted payload paths.
    $pendingPath=Join-Path $root '.projecteye-install-state.json';$pendingBytes=[IO.File]::ReadAllBytes($pendingPath)
    $pending.Root=$run;Json $pendingPath $pending
    try{Invoke-Tool @('--install','--source',$source,'--target',$root) -Failure|Out-Null}finally{[IO.File]::WriteAllBytes($pendingPath,$pendingBytes)}
    Assert $true 'Forged interrupted root rejected'
    Invoke-Tool @('--install','--source',$source,'--target',$root)|Out-Null
    Assert (@(Get-ChildItem -LiteralPath $root -Directory -Filter '.projecteye-stage-*').Count-eq0) 'Hard interruption repaired and stage recycled'
    Invoke-Tool @('--install','--source',$source,'--target',$root)|Out-Null
    Assert ((@(Get-ChildItem (Join-Path $root 'Data') -File|Get-FileHash|Select-Object -ExpandProperty Hash)-join',')-eq($dataHashes-join',')) 'Config and statistics unchanged through retries'
    Assert ((@(HistoryNames)-join',')-eq($historyBefore-join',')) 'Upgrade repair and failures create no recovery directory'
    $mutex=[Threading.Mutex]::new($true,'projecteye-enhanced')
    try{Invoke-Tool @('--check-uninstall','--target',$root) -Failure|Out-Null}finally{$mutex.ReleaseMutex();$mutex.Dispose()}
    Assert $true 'Running application mutex blocks maintenance'
    $exe=Join-Path $root 'ProjectEye.exe';$original=[IO.File]::ReadAllBytes($exe);[IO.File]::WriteAllText($exe,'modified',$utf8)
    try{Invoke-Tool @('--check-uninstall','--target',$root) -Failure|Out-Null}finally{[IO.File]::WriteAllBytes($exe,$original)}
    Assert $true 'Modified payload blocks uninstall'
    $junction=Join-Path $root 'external-link';New-Item -ItemType Junction -Path $junction -Target $fake|Out-Null
    Invoke-Tool @('--install','--source',$source,'--target',$root) -Failure|Out-Null;Assert $true 'Reparse subtree blocks upgrade before mutation'
    # Move the junction itself, never recursively enumerate or remove its target.
    $recycle=Join-Path $repo ('.recycle-bin/maintenance-tests/'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $recycle|Out-Null
    Move-Item -LiteralPath $junction -Destination (Join-Path $recycle 'external-link')
    $foreign=Join-Path $env:LOCALAPPDATA ('ProjectEyeEnhanced-Recovery/foreign-test-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $foreign|Out-Null
    Json (Join-Path $foreign 'original-location.json') @{path=(Join-Path $run 'other-install')}
    [IO.File]::WriteAllText((Join-Path $foreign 'foreign.txt'),'other installation',$utf8)
    $locked=[IO.File]::Open((Join-Path $root 'Data/statistics.db'),'Open','ReadWrite','None')
    try{$failure=Invoke-Tool @('--remove','--target',$root) -Failure;Assert ($failure.code-eq'E_LOCKED') 'Locked file returns stable Chinese failure'}finally{$locked.Dispose()}
    Assert (Test-Path (Join-Path $root 'ProjectEye.exe')) 'Lock refusal leaves installation intact'
    $removed=Invoke-Tool @('--remove','--target',$root)
    Assert (!(Test-Path -LiteralPath $root)) 'Whole application directory recycled'
    Assert (((@(HistoryNames)|Where-Object {$_-ne(Split-Path $foreign -Leaf)})-join',')-eq($historyBefore-join',')) 'Uninstall creates no recovery record'
    Assert (Test-Path (Join-Path $foreign 'foreign.txt')) 'Other recovery directory preserved'
    Add-Type -AssemblyName Microsoft.VisualBasic
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($foreign,'OnlyErrorDialogs','SendToRecycleBin')
    if(Test-Path -LiteralPath $recovery){if(@(Get-ChildItem -LiteralPath $recovery -Force).Count-eq0){[Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($recovery,'OnlyErrorDialogs','SendToRecycleBin')}}
    $key=ViewKey Registry64 $stable;Assert ($null-eq$key) 'Only own test registration removed'
    Json (Join-Path $run 'result.json') @{success=$true;checks=$checks;candidate=$Candidate;scope='Compiled identical engine with isolated registry/shortcut constants; production installer verified separately'}
}finally{Write-Output "MAINTENANCE_EVIDENCE=$run"}
