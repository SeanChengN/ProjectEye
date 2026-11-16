#ifndef Payload
  #error Payload is required
#endif
#ifndef Candidate
  #error Candidate is required
#endif
[Setup]
AppId=ProjectEyeEnhanced-beta2
AppName=ProjectEye Enhanced
AppVersion=1.0.0-beta.2
AppVerName=ProjectEye Enhanced 1.0.0-beta.2
DefaultDirName={localappdata}\Programs\ProjectEyeEnhanced\1.0.0-beta.2
PrivilegesRequired=lowest
MinVersion=10.0.17763
Uninstallable=no
CreateAppDir=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
CloseApplications=no
RestartApplications=no
OutputDir={#Candidate}
OutputBaseFilename=ProjectEye-Enhanced-1.0.0-beta.2-Setup
Compression=lzma2
SolidCompression=yes
LicenseFile={#Payload}\LICENSE
SetupIconFile=..\src\Local\ProjectEye\Resources\sunglasses.ico
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\ProjectEye Enhanced beta.2"; Filename: "{app}\ProjectEye.exe"; WorkingDir: "{app}"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta2"; ValueType: string; ValueName: "DisplayName"; ValueData: "ProjectEye Enhanced 1.0.0-beta.2"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta2"; ValueType: string; ValueName: "DisplayVersion"; ValueData: "1.0.0-beta.2"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta2"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta2"; ValueType: string; ValueName: "UninstallString"; ValueData: """{sys}\WindowsPowerShell\v1.0\powershell.exe"" -NoProfile -ExecutionPolicy Bypass -File ""{app}\Remove-TestVersion.ps1"""
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta2"; ValueType: dword; ValueName: "NoModify"; ValueData: "1"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta2"; ValueType: dword; ValueName: "NoRepair"; ValueData: "1"
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectEyeEnhanced-beta2') then
    Result := 'This test version is already installed. Exit it and use its recoverable uninstaller first.'
  else if DirExists(ExpandConstant('{app}')) then
    Result := 'Choose a new empty folder. Existing applications and settings will not be overwritten.';
end;
