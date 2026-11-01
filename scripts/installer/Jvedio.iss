; Jvedio 维护版 —— Inno Setup 安装包脚本
; 由 scripts/build-installer.ps1 调用，不要直接双击运行（需要 /D 传入源码目录）。
;
; 设计要点：
;   1. 装到 %LOCALAPPDATA%\Programs\Jvedio —— 程序要在自己目录下写 data\（库、封面、日志），
;      装进 Program Files 就得管理员权限才能跑，对普通用户是坑。
;   2. 卸载不删 data\ —— 用户的影片库和封面在里面，误删代价太大。
;   3. 未签名，首次运行会弹 SmartScreen，README 里写了怎么过。

#define AppName "Jvedio"
; 版本号可由 build-installer.ps1 用 /DAppVersion=... 覆盖，所以必须用 ifndef 包住：
; ISPP 里命令行 /D 只是"先定义"，脚本里再 #define 一次会把它盖掉。
#ifndef AppVersion
  #define AppVersion "5.4"
#endif
#define AppPublisher "muzlin00-sketch"
#define AppUrl "https://github.com/muzlin00-sketch/Jvedio-open"

#ifndef SourceDir
  #define SourceDir "..\..\release\_stage_installer"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\release"
#endif

[Setup]
; AppId 固定不变，升级安装才能覆盖旧版本而不是装两份
AppId={{8C2A5F41-6B7D-4E93-A2C5-1D9F3E7B8A64}
AppName={#AppName} 维护版
AppVersion={#AppVersion}
AppVerName={#AppName} 维护版 {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
VersionInfoVersion=5.4.0.0
VersionInfoDescription={#AppName} 维护版安装程序
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=Jvedio-{#AppVersion}-setup-win-x64
SetupIconFile=jvedio.ico
UninstallDisplayIcon={app}\Jvedio.exe
UninstallDisplayName={#AppName} 维护版 {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AllowNoIcons=yes
; 排障用：无论成功失败都写安装日志到 %TEMP%\Setup Log*.txt
SetupLogging=yes
; 不碰用户正在运行的实例（Restart Manager 会尝试关闭占用文件的进程）
CloseApplications=no
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Jvedio.exe"; Comment: "启动 {#AppName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Jvedio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Jvedio.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function DotNetRelease(): Cardinal;
var
  release: Cardinal;
  ok: Boolean;
begin
  release := 0;
  try
    if IsWin64 then
      ok := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release)
    else
      ok := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release);
  except
    ok := False;
  end;
  if ok then
    Result := release
  else
    Result := 0;
end;

// 只做提示，不做拦截：装完再补 .NET 也能用，别让用户卡在安装这一步
function InitializeSetup(): Boolean;
begin
  Result := True;
  // 4.7.2 对应 Release = 461808；Windows 10 1803 及以上系统自带
  if DotNetRelease() < 461808 then
    MsgBox('没有检测到 .NET Framework 4.7.2 或更高版本。' + #13#10 +
           'Jvedio 需要它才能运行（Windows 10 1803 及以上的系统自带）。' + #13#10#13#10 +
           '安装会继续，装好 .NET 之后即可启动。',
           mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  installDir: String;
begin
  if CurStep = ssInstall then begin
    installDir := ExpandConstant('{app}');
    // 程序运行时会往自己的目录写 data\，装在 Program Files 下会因权限失败
    if Pos(Lowercase(ExpandConstant('{commonpf}')), Lowercase(installDir)) = 1 then
      MsgBox('当前安装目录在 Program Files 下，程序运行时可能因为权限问题无法保存数据（data 目录）。' + #13#10 +
             '建议改装到默认位置：' + ExpandConstant('{localappdata}') + '\Programs\Jvedio。',
             mbInformation, MB_OK);
  end;
end;
