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
#ifdef TestIsolation
AppId=ProjectEyeEnhanced-MaintenanceTests
#else
AppId=ProjectEyeEnhanced-Test
#endif
AppName=ProjectEye Enhanced
AppVersion={#ReleaseVersion}
AppVerName=ProjectEye Enhanced {#ReleaseVersion}
DefaultDirName={code:ExistingDirectory}
UsePreviousAppDir=no
PrivilegesRequired=lowest
MinVersion=10.0.17763
Uninstallable=yes
CreateUninstallRegKey=no
UninstallFilesDir={app}\uninstall
UninstallDisplayName=ProjectEye Enhanced
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
Name: "chinesesimp"; MessagesFile: "ChineseSimplified-6.2.2.isl"
[Messages]
ConfirmUninstall=确定卸载 ProjectEye Enhanced？配置、统计、日志及安装目录内的其他文件将一并移入系统回收站。
UninstalledAll=ProjectEye Enhanced 已卸载。程序、数据及已验证归属的恢复副本已移入系统回收站。
[Files]
Source: "{#Payload}\*"; DestDir: "{tmp}\ProjectEyePayload"; Flags: dontcopy recursesubdirs createallsubdirs
[Code]
function MultiByteToWideChar(CodePage, Flags: Cardinal; Bytes: AnsiString;
  ByteCount: Integer; Wide: String; WideCount: Integer): Integer;
  external 'MultiByteToWideChar@kernel32.dll stdcall';
function DecodeUtf8(Bytes: AnsiString): String;
var Count: Integer;
begin
  Count := MultiByteToWideChar(65001, 8, Bytes, Length(Bytes), '', 0);
  SetLength(Result, Count);
  if Count > 0 then MultiByteToWideChar(65001, 8, Bytes, Length(Bytes), Result, Count);
end;
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
    '" --target "' + ExpandConstant('{app}') + '" --quiet --defer-finalize --result "' + Report + '"';
  if not Exec(Tool, Args, ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := '无法启动应用维护程序，请检查安装包是否完整。'
  else if Code <> 0 then begin
    Result := '安装未完成。请先从托盘退出程序，并核对所选目录。';
    if LoadStringFromFile(Report + '.txt', Content) then Result := DecodeUtf8(Content);
    Log(Result);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer; Report: String; Content: AnsiString; Message: String;
begin
  if CurStep = ssPostInstall then begin
    Report := ExpandConstant('{tmp}\maintenance-register.json');
    if not Exec(ExpandConstant('{app}\ProjectEye.Maintenance.exe'),
      '--register --target "' + ExpandConstant('{app}') + '" --quiet --result "' + Report + '"',
      ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('安装未完成，请重新运行同一个安装包修复卸载入口；配置和统计仍保留。');
    if LoadStringFromFile(Report + '.txt', Content) then begin
      Message := DecodeUtf8(Content);
      Log(Message);
      if Pos('保留', Message) > 0 then MsgBox(Message, mbInformation, MB_OK);
    end;
  end;
end;

var
  WorkDir, WorkTool, RemoveReport: String;

type
  TFileOperation = record
    Wnd: Integer;
    Operation: Cardinal;
    FromPath: String;
    ToPath: String;
    Flags: Word;
    Aborted: Boolean;
    Mapping: Integer;
    Title: String;
  end;
function SHFileOperation(var Op: TFileOperation): Integer;
  external 'SHFileOperationW@shell32.dll stdcall';
function RecycleDirectory(Path: String): Boolean;
var Op: TFileOperation;
begin
  Result := True;
  if not DirExists(Path) then exit;
  Op.Wnd := 0; Op.Operation := 3;
  Op.FromPath := Path + #0 + #0; Op.ToPath := '';
  Op.Flags := $40 or $10 or $4 or $400;
  Op.Aborted := False; Op.Mapping := 0; Op.Title := '';
  Result := (SHFileOperation(Op) = 0) and not Op.Aborted;
end;

function InitializeUninstall(): Boolean;
var Source: String;
begin
  WorkDir := ExpandConstant('{tmp}\ProjectEye-maintenance');
  WorkTool := WorkDir + '\ProjectEye.Maintenance.exe';
  RemoveReport := WorkDir + '\remove-result.json';
  Source := ExpandConstant('{app}\ProjectEye.Maintenance.exe');
  ForceDirectories(WorkDir);
  Result := FileCopy(Source, WorkTool, False);
  if Result then Result := GetSHA256OfFile(Source) = GetSHA256OfFile(WorkTool);
  if not Result then MsgBox('无法准备卸载维护程序，请检查程序文件是否完整。', mbError, MB_OK);
end;

function UninstallNeedRestart(): Boolean;
begin Result := False; end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer; Content: AnsiString; Message: String;
begin
  if CurUninstallStep = usUninstall then begin
    if not Exec(WorkTool, '--remove --target "' + ExpandConstant('{app}') +
      '" --quiet --result "' + RemoveReport + '"', ExpandConstant('{tmp}'),
      SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('无法启动卸载维护程序，已停止卸载。');
    if Code <> 0 then begin
      Message := '卸载未完成，请正常退出应用并关闭占用文件的程序后重试。';
      if LoadStringFromFile(RemoveReport + '.txt', Content) then Message := DecodeUtf8(Content);
      RaiseException(Message);
    end;
    if LoadStringFromFile(RemoveReport + '.txt', Content) then begin
      Message := DecodeUtf8(Content);
      if Pos('保留', Message) > 0 then MsgBox(Message, mbInformation, MB_OK);
    end;
  end;
  if CurUninstallStep = usPostUninstall then begin
    if not RecycleDirectory(ExpandConstant('{app}')) then
      RaiseException('程序文件已移除，但剩余目录未能回收，请关闭占用后重试。');
    if not RecycleDirectory(WorkDir) then
      RaiseException('程序已移除，但临时维护文件未能回收，请保留卸载日志后重试。');
  end;
end;

procedure DeinitializeUninstall();
begin
  if DirExists(WorkDir) then RecycleDirectory(WorkDir);
end;
