#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef StageDir
  #error StageDir must point to the packaged application directory
#endif
#ifndef PayloadZip
  #error PayloadZip must point to the verified portable ZIP
#endif
#ifndef ReleaseDir
  #define ReleaseDir "."
#endif

#define AppName "DSH Desktop Shell"
#define AppExeName "DSH Desktop Shell.exe"
#define WebView2ClientKey "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
AppId={{6C4BD70B-8039-4528-B56F-53F11D58CF3D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DSH Desktop Shell contributors
AppPublisherURL=https://github.com/Audrey0976/dsh-desktop-shell
AppSupportURL=https://github.com/Audrey0976/dsh-desktop-shell/issues
AppUpdatesURL=https://github.com/Audrey0976/dsh-desktop-shell/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#ReleaseDir}
OutputBaseFilename=DSH-Desktop-Shell-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\windows\DSH.Desktop.Shell\assets\dsh-shell.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchiveExtraction=full
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=DSHDesktopShell-04B25E6B-66D1-4B36-93D0-D75F08278C72
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=Unofficial community desktop shell for DeepSeek Harness
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadZip}"; DestDir: "{tmp}"; DestName: "dsh-desktop-shell-payload.zip"; Flags: ignoreversion deleteafterinstall nocompression

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNet48Url = 'https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48';
  WebView2Url = 'https://developer.microsoft.com/en-us/microsoft-edge/webview2/consumer/';

function DotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= 528040);
end;

function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result := (RegQueryStringValue(HKLM32, '{#WebView2ClientKey}', 'pv', Version)
      or RegQueryStringValue(HKCU, '{#WebView2ClientKey}', 'pv', Version))
    and (Version <> '') and (Version <> '0.0.0.0');
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := False;
  if not DotNet48Installed then
  begin
    MsgBox('DSH Desktop Shell 需要 Microsoft .NET Framework 4.8 或更高版本。安装程序将打开微软官方下载页；安装完成后请重新运行本安装包。', mbError, MB_OK);
    ShellExec('open', DotNet48Url, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Exit;
  end;
  if not WebView2Installed then
  begin
    MsgBox('DSH Desktop Shell 需要 Microsoft Edge WebView2 Evergreen Runtime。安装程序将打开微软官方下载页；安装完成后请重新运行本安装包。', mbError, MB_OK);
    ShellExec('open', WebView2Url, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Exit;
  end;
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Payload: String;
begin
  if CurStep <> ssPostInstall then
    Exit;
  Payload := ExpandConstant('{tmp}\dsh-desktop-shell-payload.zip');
  ExtractArchive(Payload, ExpandConstant('{app}'), '', True, nil);
  { The same verified ZIP serves both release forms. Removing this marker
    switches the installed edition back to per-user LocalAppData storage. }
  DeleteFile(ExpandConstant('{app}\portable.marker'));
  DeleteFile(ExpandConstant('{app}\PORTABLE-DATA-WARNING.zh-CN.txt'));
end;
