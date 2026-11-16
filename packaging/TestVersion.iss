#ifndef Payload
  #error Payload is required
#endif
#ifndef Candidate
  #error Candidate is required
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
[Setup]
AppId=ProjectEyeEnhanced-Test
AppName=ProjectEye Enhanced
AppVersion={#ReleaseVersion}
AppVerName=ProjectEye Enhanced {#ReleaseVersion}
DefaultDirName={code:ExistingDirectory}
UsePreviousAppDir=no
PrivilegesRequired=lowest
MinVersion=10.0.17763
Uninstallable=no
CreateAppDir=yes
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
OutputDir={#Candidate}
OutputBaseFilename=ProjectEye-Enhanced-{#ReleaseVersion}-Setup
Compression=lzma2
SolidCompression=yes
LicenseFile={#Payload}\LICENSE
SetupIconFile=..\src\Local\ProjectEye\Resources\sunglasses.ico
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "{#Payload}\*"; DestDir: "{tmp}\ProjectEyePayload"; Flags: dontcopy recursesubdirs createallsubdirs
[Code]
function ExistingDirectory(Param: String): String;
var
  View, I, Hive: Integer;
  Key, Location: String;
  Names: array[0..2] of String;
  Content: AnsiString;
begin
  Result := ExpandConstant('{localappdata}\Programs\ProjectEyeEnhanced\Test');
  Names[0] := 'ProjectEyeEnhanced-Test';
  Names[1] := 'ProjectEyeEnhanced-beta2';
  Names[2] := 'ProjectEyeEnhanced-beta1';
  for I := 0 to 2 do
    for View := 0 to 1 do begin
      if View = 0 then Hive := HKCU64 else Hive := HKCU32;
      Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + Names[I];
      if RegQueryStringValue(Hive, Key, 'InstallLocation', Location) then
        if LoadStringFromFile(AddBackslash(Location) + 'test-package.json', Content) then
          if Pos('ProjectEye Enhanced', String(Content)) > 0 then begin
            Result := Location;
            exit;
          end;
    end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Tool, Args, Report: String;
  Content: AnsiString;
begin
  Result := '';
  ExtractTemporaryFiles('{tmp}\ProjectEyePayload\*');
  Tool := ExpandConstant('{tmp}\ProjectEyePayload\ProjectEye.Maintenance.exe');
  Report := ExpandConstant('{tmp}\maintenance-result.json');
  Args := '--install --source "' + ExpandConstant('{tmp}\ProjectEyePayload') +
    '" --target "' + ExpandConstant('{app}') + '" --quiet --result "' + Report + '"';
  if not Exec(Tool, Args, ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := 'Cannot launch application maintenance tool.'
  else if Code <> 0 then begin
    Result := 'Installation refused or rolled back. Exit the tray application normally and check selected folder.';
    if LoadStringFromFile(Report, Content) then Result := Result + #13#10 + String(Content);
    Log(Result);
  end;
end;
