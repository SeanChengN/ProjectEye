#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Candidate)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Candidate=(Resolve-Path -LiteralPath $Candidate).Path
$release=Get-Content (Join-Path $Candidate 'result.json') -Raw -Encoding utf8|ConvertFrom-Json
if(!$release.success){throw 'Successful candidate required'}
$run=Join-Path $repo ('artifacts/installed-release-tests/'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'));New-Item -ItemType Directory -Path $run|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Json($Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 30),$utf8)}
$checks=[Collections.Generic.List[string]]::new()
function Assert($Value,$Name){if(!$Value){throw "FAILED: $Name"};$checks.Add($Name);Write-Output "PASS: $Name"}
$registry='Software\ProjectEyeEnhanced-MaintenanceTests\Uninstall\'
function TreeHash($Root){if(Test-Path -LiteralPath $Root){@(Get-ChildItem -LiteralPath $Root -Recurse -File|Sort-Object FullName|ForEach-Object{[ordered]@{path=[IO.Path]::GetRelativePath($Root,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}else{@()}}
function RegistrySnapshot($Hive,$Path){
    $items=[Collections.Generic.List[object]]::new()
    foreach($view in @('Registry32','Registry64')){
        $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive,$view)
        function Walk($Base,$Current){
            $key=$Base.OpenSubKey($Current)
            if(!$key){$items.Add([ordered]@{view=$view;path=$Current;exists=$false});return}
            $values=@($key.GetValueNames()|Sort-Object|ForEach-Object{[ordered]@{name=$_;kind=$key.GetValueKind($_).ToString();value=$key.GetValue($_,$null,'DoNotExpandEnvironmentNames')}})
            $items.Add([ordered]@{view=$view;path=$Current;values=$values})
            foreach($name in $key.GetSubKeyNames()|Sort-Object){Walk $Base ($Current+'\'+$name)};$key.Dispose()
        };Walk $base $Path;$base.Dispose()
    };return $items.ToArray()
}
function State {
    $actual=Join-Path $env:LOCALAPPDATA 'Programs/ProjectEyeEnhanced/1.0.0-beta'
    [ordered]@{
        system32=TreeHash "$env:windir/System32/WindowsPowerShell/v1.0"
        syswow64=TreeHash "$env:windir/SysWOW64/WindowsPowerShell/v1.0"
        powerShellRegistry=RegistrySnapshot LocalMachine 'SOFTWARE\Microsoft\PowerShell'
        powerShellClasses=@('Microsoft.PowerShellScript.1','Microsoft.PowerShellConsole.1','Applications\powershell.exe','Applications\powershell_ise.exe')|ForEach-Object{RegistrySnapshot LocalMachine ('SOFTWARE\Classes\'+$_)}
        userPowerShell=RegistrySnapshot CurrentUser 'Software\Microsoft\PowerShell'
        actualStable=RegistrySnapshot CurrentUser 'Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-Test'
        legacy1=RegistrySnapshot CurrentUser ($registry+'ProjectEyeEnhanced-beta1')
        legacy2=RegistrySnapshot CurrentUser ($registry+'ProjectEyeEnhanced-beta2')
        actualInstallation=TreeHash $actual
        currentInstallation=TreeHash (Join-Path $env:LOCALAPPDATA 'Programs/ProjectEyeEnhanced/Test')
        shortcutHashes=@(@([Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('Startup'),[Environment]::GetFolderPath('Desktop'))|ForEach-Object{Get-ChildItem -LiteralPath $_ -Filter '*ProjectEye*.lnk' -File|Sort-Object FullName|Get-FileHash|Select-Object Path,Hash})
    }
}
# Build identical code with isolated registry/shortcut constants, then compile the
# same Inno script. No actual installation registration is temporarily replaced.
$production=$release.payload
$payload=Join-Path $run 'payload';Copy-Item -LiteralPath $production -Destination $payload -Recurse
$compiler=Join-Path $run 'maintenance';New-Item -ItemType Directory -Path $compiler|Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'packaging/Maintenance') -File|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $compiler}
Copy-Item -LiteralPath (Join-Path $Candidate 'maintenance-source/BuildIdentity.cs') -Destination $compiler
$build=Get-Content (Join-Path $release.buildDirectory 'result.json') -Raw -Encoding utf8|ConvertFrom-Json
& $build.msbuild (Join-Path $compiler 'Maintenance.csproj') /t:Build /nologo /v:minimal /p:DefineConstants=MAINTENANCE_TESTS "/p:EvidencePath=$compiler" "/p:TargetFrameworkRootPath=$($build.referenceRoot)" "/p:IdentitySource=$compiler/BuildIdentity.cs" "/p:ProductIcon=$repo/src/Local/ProjectEye/Resources/sunglasses.ico" > (Join-Path $run 'maintenance-build.log')
if($LASTEXITCODE-ne0){throw 'Test tool build failed'}
Copy-Item -LiteralPath (Join-Path $compiler 'bin/ProjectEye.Maintenance.exe') -Destination (Join-Path $payload 'ProjectEye.Maintenance.exe') -Force
$manifest=Get-Content (Join-Path $payload 'payload-manifest.json') -Raw -Encoding utf8|ConvertFrom-Json
$entry=$manifest|Where-Object path -eq 'ProjectEye.Maintenance.exe';$entry.sha256=(Get-FileHash (Join-Path $payload $entry.path)).Hash;$entry.bytes=(Get-Item (Join-Path $payload $entry.path)).Length
Json (Join-Path $payload 'payload-manifest.json') $manifest
$plan=Get-Content (Join-Path $Candidate 'plan.json') -Raw -Encoding utf8|ConvertFrom-Json
& $plan.iscc ("/DPayload=$payload") ("/DCandidate=$run") ("/DReleaseVersion=$($release.version)") ("/DReleaseFileVersion=$((Get-Content (Join-Path $repo 'packaging/test-release.json') -Raw -Encoding utf8|ConvertFrom-Json).fileVersion)") /DTestIsolation=1 (Join-Path $repo 'packaging/TestVersion.iss') > (Join-Path $run 'inno-build.log')
if($LASTEXITCODE-ne0){throw 'Test installer build failed'}
$release.payload=$payload;$release.setup=Join-Path $run ('ProjectEye-Enhanced-'+$release.version+'-Setup.exe')
$historyRoot=Join-Path $env:LOCALAPPDATA 'ProjectEyeEnhanced-Recovery'
$historyBefore=@(Get-ChildItem -LiteralPath $historyRoot -Directory -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Name|Sort-Object)
$before=State;Json (Join-Path $run 'protected-before.json') $before
$root=Join-Path $run '中文用户路径/安装目录'
$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser','Registry64');$old=$base.OpenSubKey($registry+'ProjectEyeEnhanced-Test')
if($old){$oldRoot=[string]$old.GetValue('InstallLocation');$old.Dispose();if(!$oldRoot.StartsWith((Join-Path $repo 'artifacts/installed-release-tests/').Replace('/','\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Unowned previous fixture'};$root=$oldRoot}
$base.Dispose()
function Setup($Label){
    $p=Start-Process -FilePath $release.setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$root+'"'),('/LOG="'+(Join-Path $run ($Label+'.log'))+'"')) -WindowStyle Hidden -PassThru -Wait
    Assert ($p.ExitCode-eq0) "Setup EXE $Label"
    $expected=Get-Content (Join-Path $release.payload 'payload-manifest.json') -Raw -Encoding utf8|ConvertFrom-Json
    foreach($entry in $expected){if((Get-FileHash -LiteralPath (Join-Path $root $entry.path)).Hash-ne$entry.sha256){throw "Payload mismatch: $($entry.path)"}}
    Assert ((Get-FileHash (Join-Path $root 'payload-manifest.json')).Hash-eq(Get-FileHash (Join-Path $release.payload 'payload-manifest.json')).Hash) "All extracted payload hashes $Label"
}
function Uninstall($Label){
    $exe=Join-Path $root 'uninstall/unins000.exe'
    $p=Start-Process -FilePath $exe -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $run ($Label+'.log'))+'"')) -WindowStyle Hidden -PassThru -Wait
    Assert ($p.ExitCode-eq0) "Inno uninstall $Label"
    $until=[DateTime]::UtcNow.AddSeconds(8)
    while((Test-Path -LiteralPath $root)-and[DateTime]::UtcNow-lt$until){Start-Sleep -Milliseconds 100}
    Assert (!(Test-Path -LiteralPath $root)) "Whole directory absent after $Label"
}
try{
    foreach($folder in @('licenses','UI','x64','x86')){New-Item -ItemType Directory -Path (Join-Path $root $folder) -Force|Out-Null}
    Setup 'fresh-install'
    $key=Get-ItemProperty ('HKCU:\'+$registry+'ProjectEyeEnhanced-Test')
    Assert ($key.UninstallString-eq('"'+(Join-Path $root 'uninstall/unins000.exe')+'"')) 'Own Inno EXE registered without generic host'
    $locked=[IO.File]::Open((Join-Path $root 'LICENSE'),'Open','Read','None')
    try{
        $p=Start-Process -FilePath (Join-Path $root 'uninstall/unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $run 'locked-uninstall.log')+'"')) -WindowStyle Hidden -PassThru -Wait
        Assert ($p.ExitCode-ne0) 'Real uninstall reports failure while file locked'
        Assert (Test-Path (Join-Path $root 'ProjectEye.exe')) 'Failed real uninstall preserves program'
        $errorText=Get-Content (Join-Path $run 'locked-uninstall.log') -Raw -Encoding utf8
        Assert ($errorText.Contains('文件正在被其他程序占用')-and!$errorText.Contains('Uninstallation process succeeded')) 'Chinese lock error without false success'
    }finally{$locked.Dispose()}
    Uninstall 'fresh-uninstall'
    for($cycle=2;$cycle-le3;$cycle++){Setup ('cycle-'+$cycle);Uninstall ('cycle-'+$cycle)}
    $legacy1=Join-Path $repo 'artifacts/test-release/20261116-191450-590-1.0.0-beta.1/ProjectEye-Enhanced-1.0.0-beta.1'
    New-Item -ItemType Directory -Path $root -Force|Out-Null
    Copy-Item -Path (Join-Path $legacy1 '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data') -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options>beta1</Options>',$utf8)
    $hash1=(Get-FileHash (Join-Path $root 'Data/config.xml')).Hash
    Setup 'beta1-stable-upgrade'
    Assert ((Get-FileHash (Join-Path $root 'Data/config.xml')).Hash-eq$hash1) 'Beta.1 stable upgrade preserves configuration'
    Uninstall 'beta1-upgraded-uninstall'
    $legacy=Join-Path $repo 'artifacts/test-release/20261116-205717-242-1.0.0-beta.2/ProjectEye-Enhanced-1.0.0-beta.2'
    New-Item -ItemType Directory -Path $root -Force|Out-Null
    Copy-Item -Path (Join-Path $legacy '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data'),(Join-Path $root 'Log')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options/>',$utf8)
    [IO.File]::WriteAllBytes((Join-Path $root 'Data/statistics.db'),[byte[]](0..255))
    [IO.File]::WriteAllText((Join-Path $root 'Log/test.log'),'log',$utf8)
    [IO.File]::WriteAllText((Join-Path $root 'user-file.txt'),'preserve me',$utf8)
    $dataBefore=TreeHash (Join-Path $root 'Data')
    Setup 'beta2-upgrade';Setup 'repeat-current'
    Assert (($dataBefore|ConvertTo-Json -Compress)-eq((TreeHash (Join-Path $root 'Data'))|ConvertTo-Json -Compress)) 'Real setup preserves configuration and statistics'
    Assert (!(Test-Path (Join-Path $root 'Remove-TestVersion.ps1'))) 'Real upgrade removes old host script'
    Uninstall 'upgraded-uninstall'
    $legacy3=Join-Path $repo 'artifacts/test-release/20261116-231159-491-1.0.0-beta.3/ProjectEye-Enhanced-1.0.0-beta.3'
    New-Item -ItemType Directory -Path $root -Force|Out-Null
    Copy-Item -Path (Join-Path $legacy3 '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options>beta3</Options>',$utf8)
    $hash3=(Get-FileHash (Join-Path $root 'Data/config.xml')).Hash
    Setup 'beta3-upgrade'
    Assert ((Get-FileHash (Join-Path $root 'Data/config.xml')).Hash-eq$hash3) 'Beta.3 upgrade retains configuration'
    Uninstall 'beta3-upgraded-uninstall'
    $legacy4=Join-Path $repo 'artifacts/test-release/20261117-112632-008-1.0.0-beta.4/ProjectEye-Enhanced-1.0.0-beta.4'
    New-Item -ItemType Directory -Path $root -Force|Out-Null
    Copy-Item -Path (Join-Path $legacy4 '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options>beta4</Options>',$utf8)
    $hash4=(Get-FileHash (Join-Path $root 'Data/config.xml')).Hash
    Setup 'beta4-upgrade'
    Assert ((Get-FileHash (Join-Path $root 'Data/config.xml')).Hash-eq$hash4) 'Beta.4 upgrade retains configuration'
    Assert (!(Test-Path (Join-Path $root 'README.md'))-and!(Test-Path (Join-Path $root 'CHANGELOG.md'))-and!(Test-Path (Join-Path $root 'licenses/.gitattributes'))) 'Upgrade retires only trusted obsolete documents'
    Assert (Test-Path (Join-Path $root 'LICENSE')) 'Required copyright notice retained'
    Uninstall 'beta4-upgraded-uninstall'
    $legacy5=Join-Path $repo 'artifacts/test-release/20261117-150529-094-1.0.0-beta.5/ProjectEye-Enhanced-1.0.0-beta.5'
    New-Item -ItemType Directory -Path $root -Force|Out-Null
    Copy-Item -Path (Join-Path $legacy5 '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options>beta5</Options>',$utf8)
    $hash5=(Get-FileHash (Join-Path $root 'Data/config.xml')).Hash
    Setup 'beta5-upgrade'
    Assert ((Get-FileHash (Join-Path $root 'Data/config.xml')).Hash-eq$hash5) 'Beta.5 upgrade retains configuration'
    Assert (!(Test-Path (Join-Path $root '.projecteye-install-state.json'))) 'Final Inno registration removes interrupted state'
    Uninstall 'beta5-upgraded-uninstall'
    $legacy6=Join-Path $repo 'artifacts/test-release/20261119-203703-992-1.0.0-beta.6/ProjectEye-Enhanced-1.0.0-beta.6'
    New-Item -ItemType Directory -Path $root -Force|Out-Null
    Copy-Item -Path (Join-Path $legacy6 '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data'),(Join-Path $root 'Log')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options>beta6</Options>',$utf8)
    [IO.File]::WriteAllBytes((Join-Path $root 'Data/statistics.db'),[byte[]](0..255))
    $hash6=TreeHash (Join-Path $root 'Data')
    Setup 'beta6-stable-upgrade';Setup 'stable-repair'
    Assert (($hash6|ConvertTo-Json -Compress)-eq((TreeHash (Join-Path $root 'Data'))|ConvertTo-Json -Compress)) 'Beta.6 stable upgrade and repair preserve data'
    Uninstall 'stable-upgraded-uninstall'
    $historyAfter=@(Get-ChildItem -LiteralPath $historyRoot -Directory -ErrorAction SilentlyContinue|Select-Object -ExpandProperty Name|Sort-Object)
    Assert (($historyBefore-join',')-eq($historyAfter-join',')) 'No install upgrade repair or uninstall creates Recovery directories'
    $after=State;Json (Join-Path $run 'protected-after.json') $after
    Assert (($before|ConvertTo-Json -Depth 30 -Compress)-eq($after|ConvertTo-Json -Depth 30 -Compress)) 'System PowerShell files/registrations and actual beta.2 install/links untouched'
    Json (Join-Path $run 'result.json') @{success=$true;candidate=$Candidate;checks=$checks;deepHiBit='NOT TESTED';scope='Identical Inno and maintenance source compiled with isolated product/registry constants; real install/uninstall in Chinese directory, actual user installation preserved'}
}finally{Write-Output "INSTALLED_RELEASE_EVIDENCE=$run"}
