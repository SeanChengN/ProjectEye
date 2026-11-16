# Compatible with Windows PowerShell 5.1. Remove only this exact installed beta.
[CmdletBinding()]
param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath($PSScriptRoot)
$markerPath = Join-Path $app 'test-package.json'
$marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($marker.product -ne 'ProjectEye Enhanced' -or $marker.version -ne '1.0.0-beta.1') { throw 'Unrecognized test installation.' }
if (!(Test-Path -LiteralPath (Join-Path $app 'ProjectEye.exe'))) { throw 'Application not found.' }
$running = @(Get-Process ProjectEye -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $app 'ProjectEye.exe') })
if ($running.Count) { throw 'Exit ProjectEye Enhanced from its tray menu before uninstalling.' }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'ProjectEye Enhanced beta.1.lnk'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'ProjectEye Enhanced.lnk'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta1'
if ($Preview) { Write-Output "Recycle application and its Data/Log: $app"; Write-Output "Remove owned shortcut and registration: $shortcut; $key"; return }
Add-Type -AssemblyName Microsoft.VisualBasic
# Recycle the whole application, including all user data, rather than deleting it.
[Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($app, 'OnlyErrorDialogs', 'SendToRecycleBin')
foreach ($ownedShortcut in @($shortcut, $startupShortcut)) {
    if (Test-Path -LiteralPath $ownedShortcut) {
        $shell = New-Object -ComObject WScript.Shell
        if ($shell.CreateShortcut($ownedShortcut).TargetPath -eq (Join-Path $app 'ProjectEye.exe')) {
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($ownedShortcut, 'OnlyErrorDialogs', 'SendToRecycleBin')
        }
    }
}
if (Test-Path -LiteralPath $key) {
    $registration = Get-ItemProperty -LiteralPath $key
    if ($registration.InstallLocation -eq $app) { Remove-Item -LiteralPath $key }
}
Write-Output 'Test version moved to the Windows Recycle Bin. Restore it there if needed.'
