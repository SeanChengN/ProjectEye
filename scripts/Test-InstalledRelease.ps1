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
$registry='Software\Microsoft\Windows\CurrentVersion\Uninstall\'
foreach($view in @('Registry32','Registry64')){ $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser',$view);$key=$base.OpenSubKey($registry+'ProjectEyeEnhanced-Test');if($key){throw 'Stable channel already installed; use isolated Windows for this test'};$base.Dispose() }
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
        legacy1=RegistrySnapshot CurrentUser ($registry+'ProjectEyeEnhanced-beta1')
        legacy2=RegistrySnapshot CurrentUser ($registry+'ProjectEyeEnhanced-beta2')
        actualInstallation=TreeHash $actual
        shortcutHashes=@(@([Environment]::GetFolderPath('Programs'),[Environment]::GetFolderPath('Startup'),[Environment]::GetFolderPath('Desktop'))|ForEach-Object{Get-ChildItem -LiteralPath $_ -Filter '*ProjectEye*.lnk' -File|Sort-Object FullName|Get-FileHash|Select-Object Path,Hash})
    }
}
$before=State;Json (Join-Path $run 'protected-before.json') $before
$root=Join-Path $run 'installed'
function Setup($Label){
    $p=Start-Process -FilePath $release.setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$root+'"'),('/LOG="'+(Join-Path $run ($Label+'.log'))+'"')) -WindowStyle Hidden -PassThru -Wait
    Assert ($p.ExitCode-eq0) "Setup EXE $Label"
    $expected=Get-Content (Join-Path $release.payload 'payload-manifest.json') -Raw -Encoding utf8|ConvertFrom-Json
    foreach($entry in $expected){if((Get-FileHash -LiteralPath (Join-Path $root $entry.path)).Hash-ne$entry.sha256){throw "Payload mismatch: $($entry.path)"}}
    Assert ((Get-FileHash (Join-Path $root 'payload-manifest.json')).Hash-eq(Get-FileHash (Join-Path $release.payload 'payload-manifest.json')).Hash) "All extracted payload hashes $Label"
}
function Uninstall($Label){
    $exe=Join-Path $root 'ProjectEye.Maintenance.exe';$result=Join-Path $run ($Label+'.json')
    $p=Start-Process -FilePath $exe -ArgumentList ('--uninstall --quiet --result "'+$result+'"') -WindowStyle Hidden -PassThru -Wait
    Assert ($p.ExitCode-eq0) "Own EXE launches helper $Label"
    $seen=[Collections.Generic.HashSet[string]]::new();$until=[DateTime]::UtcNow.AddSeconds(30)
    while(!(Test-Path -LiteralPath $result)-and[DateTime]::UtcNow-lt$until){
        Get-Process -Name 'ProjectEye.Maintenance' -ErrorAction SilentlyContinue|ForEach-Object{if($_.Path){[void]$seen.Add($_.Path)}};Start-Sleep -Milliseconds 50
    }
    if(!(Test-Path -LiteralPath $result)){throw 'Helper result missing'}
    $data=Get-Content -LiteralPath $result -Raw -Encoding utf8|ConvertFrom-Json
    Assert $data.success "Production helper removal $Label"
    Json (Join-Path $run ($Label+'-observed-helpers.json')) @($seen)
    Assert (!(Test-Path (Join-Path $root 'ProjectEye.exe'))-and!(Test-Path $exe)) "Program files removed $Label"
    return $data
}
try{
    Setup 'fresh-install'
    $key=Get-ItemProperty ('HKCU:\'+$registry+'ProjectEyeEnhanced-Test')
    Assert ($key.UninstallString-eq('"'+(Join-Path $root 'ProjectEye.Maintenance.exe')+'" --uninstall')) 'Production uninstall entry has no generic host'
    $removed=Uninstall 'fresh-uninstall'
    $legacy=Join-Path $repo 'artifacts/test-release/20261116-205717-242-1.0.0-beta.2/ProjectEye-Enhanced-1.0.0-beta.2'
    Copy-Item -Path (Join-Path $legacy '*') -Destination $root -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $root 'Data'),(Join-Path $root 'Log')|Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Data/config.xml'),'<Options/>',$utf8)
    [IO.File]::WriteAllBytes((Join-Path $root 'Data/statistics.db'),[byte[]](0..255))
    [IO.File]::WriteAllText((Join-Path $root 'Log/test.log'),'log',$utf8)
    [IO.File]::WriteAllText((Join-Path $root 'user-file.txt'),'preserve me',$utf8)
    $dataBefore=TreeHash (Join-Path $root 'Data')
    Setup 'beta2-upgrade';Setup 'repeat-beta3'
    Assert (($dataBefore|ConvertTo-Json -Compress)-eq((TreeHash (Join-Path $root 'Data'))|ConvertTo-Json -Compress)) 'Real setup preserves configuration and statistics'
    Assert (!(Test-Path (Join-Path $root 'Remove-TestVersion.ps1'))) 'Real upgrade removes old host script'
    $removed=Uninstall 'upgraded-uninstall'
    Assert (($dataBefore|ConvertTo-Json -Compress)-eq((TreeHash (Join-Path $removed.recovery 'Data'))|ConvertTo-Json -Compress)) 'Production uninstall preserves data hashes'
    Assert (Test-Path (Join-Path $root 'user-file.txt')) 'Production uninstall leaves unknown files'
    Assert (Test-Path (Join-Path $removed.recovery 'Log/test.log')) 'Production uninstall retains logs'
    $after=State;Json (Join-Path $run 'protected-after.json') $after
    Assert (($before|ConvertTo-Json -Depth 30 -Compress)-eq($after|ConvertTo-Json -Depth 30 -Compress)) 'System PowerShell files/registrations and actual beta.2 install/links untouched'
    Json (Join-Path $run 'result.json') @{success=$true;candidate=$Candidate;checks=$checks;deepHiBit='NOT TESTED';scope='Real setup and own uninstall EXEs in isolated directory; existing legacy registrations preserved'}
}finally{Write-Output "INSTALLED_RELEASE_EVIDENCE=$run"}
