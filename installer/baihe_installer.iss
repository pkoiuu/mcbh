; ============================================================
; 白鹤服务器启动器 — Inno Setup 安装脚本
; 安装到 %LOCALAPPDATA%\BaiheServer (无需管理员权限)
; 包含: 启动器 + JRE 21 + WebView2 Runtime + .minecraft 游戏全量文件
; 做到开箱即用 — 无需任何额外下载
; 注意: 路径相对于本 .iss 文件所在目录 (installer/)
; ============================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.1.2"
#endif

[Setup]
; 应用信息
AppName=白鹤服务器启动器
AppVersion={#MyAppVersion}
AppPublisher=白鹤服务器
AppPublisherURL=https://github.com/pkoiuu/mcbh
AppSupportURL=https://github.com/pkoiuu/mcbh
AppUpdatesURL=https://github.com/pkoiuu/mcbh/releases

; 唯一标识 — 用于升级检测（同一 AppId 覆盖安装视为升级而非新装）
AppId={{8F2B7A3C-1D4E-4F5B-9A6C-7E8F9A0B1C2D}
UsePreviousAppDir=yes
UsePreviousTasks=yes

; 版本信息 (用于安装包文件属性)
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=白鹤服务器
VersionInfoProductName=白鹤服务器启动器
VersionInfoProductVersion={#MyAppVersion}.0

; 安装目录 — 用户目录，无需管理员权限
DefaultDirName={localappdata}\BaiheServer
DefaultGroupName=白鹤服务器启动器
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

; 卸载
UninstallDisplayName=白鹤服务器启动器
UninstallDisplayIcon={app}\Baihe.exe
CreateUninstallRegKey=yes

; 压缩
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; 界面 — 路径相对于 .iss 文件目录
WizardStyle=modern
ShowLanguageDialog=no
LanguageDetectionMethod=none
SetupIconFile=..\installer_resources\icon.ico
WizardImageFile=..\installer_assets\wizimage.bmp
WizardSmallImageFile=..\installer_assets\wizsmallimage.bmp

; 输出 — 到仓库根目录的 dist/
OutputDir=..\dist
OutputBaseFilename=白鹤服务器启动器_Setup_v{#MyAppVersion}
CloseApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "..\installer_resources\ChineseSimplified.isl"

[Files]
; 启动器主程序和所有依赖文件 (包含 wwwroot、runtimes 等)
; 排除: JRE、WebView2 安装程序、运行时生成的用户配置文件（升级时必须保留）
Source: "..\dist\launcher\*"; DestDir: "{app}"; Excludes: "jre\*,MicrosoftEdgeWebView2RuntimeInstallerX64.exe,settings.json,account.json,current_instance.txt,*.log,debug-*.txt,cache\*,Baihe.exe.WebView2\*"; Flags: ignoreversion recursesubdirs createallsubdirs

; JRE 21 运行时 (jlink 最小化构建，18 模块)
Source: "..\dist\launcher\jre\*"; DestDir: "{app}\jre"; Flags: ignoreversion recursesubdirs createallsubdirs

; WebView2 离线安装程序 (约 2MB，安装时自动检测并安装)
Source: "..\dist\launcher\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{app}"; Flags: ignoreversion

; ===== .minecraft 核心游戏文件 — 始终更新（升级时覆盖为新版本）=====
; 版本文件 (versions/) — Minecraft 1.21.8 + Fabric Loader
Source: "..\dist\.minecraft\versions\*"; DestDir: "{app}\.minecraft\versions"; Flags: recursesubdirs createallsubdirs ignoreversion

; 库文件 (libraries/) — 76 个库 + 9 个原生库
Source: "..\dist\.minecraft\libraries\*"; DestDir: "{app}\.minecraft\libraries"; Flags: recursesubdirs createallsubdirs ignoreversion

; 资源文件 (assets/) — 3985 个资源对象
Source: "..\dist\.minecraft\assets\*"; DestDir: "{app}\.minecraft\assets"; Flags: recursesubdirs createallsubdirs ignoreversion

; 预装 Mod (mods/) — 更新到最新版本（用户自添加的 mod 不受影响）
Source: "..\dist\.minecraft\mods\*"; DestDir: "{app}\.minecraft\mods"; Flags: ignoreversion recursesubdirs createallsubdirs

; ===== .minecraft 用户数据 — 仅首次安装时写入，升级/卸载时均保留用户已有配置 =====
; 注意: onlyifdoesntexist 控制安装时不覆盖已有文件
;       uninsneveruninstall 确保卸载时不删除这些用户配置文件
; 游戏设置 (options.txt) — 渲染距离、按键绑定、音量等
Source: "..\dist\.minecraft\options.txt"; DestDir: "{app}\.minecraft"; Flags: onlyifdoesntexist uninsneveruninstall

; 服务器列表 (servers.dat)
Source: "..\dist\.minecraft\servers.dat"; DestDir: "{app}\.minecraft"; Flags: onlyifdoesntexist uninsneveruninstall

; Mod 配置 (config/) — 各 Mod 的配置文件
Source: "..\dist\.minecraft\config\*"; DestDir: "{app}\.minecraft\config"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall

; 启动器配置 (launcher_profiles.json)
Source: "..\dist\.minecraft\launcher_profiles.json"; DestDir: "{app}\.minecraft"; Flags: onlyifdoesntexist uninsneveruninstall

[InstallDelete]
; 升级时清理旧版内置版本目录（1.21.3 → 1.21.8 迁移；用户游戏存档在 saves/ 不受影响）
Type: filesandordirs; Name: "{app}\.minecraft\versions\fabric-loader-0.16.14-1.21.3"
Type: filesandordirs; Name: "{app}\.minecraft\versions\1.21.3"
; 清理旧版 1.21.3 模组（含用户自加的 1.21.3 模组），避免与新的 1.21.8 模组共存导致重复加载崩溃
; 注: 部分旧模组文件名不含 MC 版本标记，需精确匹配
Type: files; Name: "{app}\.minecraft\mods\*1.21.3*"
Type: files; Name: "{app}\.minecraft\mods\*1.21.2*"
Type: files; Name: "{app}\.minecraft\mods\cloth-config-16.0.143-fabric.jar"
Type: files; Name: "{app}\.minecraft\mods\modmenu-12.0.1.jar"

[Icons]
; 桌面快捷方式 — 使用 userdesktop 避免 lowest 权限下 commondesktop 的问题
Name: "{userdesktop}\白鹤服务器启动器"; Filename: "{app}\Baihe.exe"; IconFilename: "{app}\icon.ico"; Comment: "白鹤服务器专用启动器"

; 开始菜单快捷方式
Name: "{group}\白鹤服务器启动器"; Filename: "{app}\Baihe.exe"; IconFilename: "{app}\icon.ico"; Comment: "白鹤服务器专用启动器"
Name: "{group}\卸载白鹤服务器启动器"; Filename: "{uninstallexe}"

[Run]
; 安装 WebView2 Runtime (如果未安装)
Filename: "{app}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; Check: not WebView2Installed(); Flags: waituntilterminated runhidden
; 安装完成后启动
Filename: "{app}\Baihe.exe"; Description: "{cm:LaunchProgram,白鹤服务器启动器}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时仅清理运行时产生的临时数据，保留用户数据 (saves/options.txt/screenshots/config 等)
Type: filesandordirs; Name: "{app}\.minecraft\logs"
Type: filesandordirs; Name: "{app}\.minecraft\crash-reports"
Type: filesandordirs; Name: "{app}\.minecraft\downloads"

[Code]
const
  AppProcessName = 'Baihe.exe';

// 检测进程是否在运行
function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq ' + AppProcessName + '" 2>NUL | FIND "' + AppProcessName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// 强制关闭进程（含子进程 — WebView2 渲染进程等）
function KillApp(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C taskkill /IM "' + AppProcessName + '" /T /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// 检测 WebView2 Runtime 是否已安装
function WebView2Installed(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
    or RegKeyExists(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

// ===== 安装/升级前：检测并关闭运行中的启动器 =====
function InitializeSetup(): Boolean;
begin
  Result := True;

  if IsAppRunning() then
  begin
    if MsgBox('检测到白鹤服务器启动器正在运行。' #13#10#13#10 '升级需要先关闭正在运行的启动器。' #13#10 '是否立即关闭并继续升级？', mbConfirmation, MB_YESNO) = IDYES then
    begin
      KillApp();
      Sleep(1000); // 等待进程完全退出和文件句柄释放
    end
    else
    begin
      MsgBox('请手动关闭白鹤服务器启动器后重新运行安装程序。', mbInformation, MB_OK);
      Result := False;
    end;
  end;
end;

// ===== 文件复制前：最终保障 — 确保进程已关闭 =====
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Attempts: Integer;
begin
  Result := '';

  if IsAppRunning() then
  begin
    for Attempts := 1 to 3 do
    begin
      KillApp();
      Sleep(500);
      if not IsAppRunning() then
        Break;
    end;

    if IsAppRunning() then
      Result := '无法关闭正在运行的白鹤服务器启动器，请手动关闭后重试。';
  end;
end;

// ===== 卸载前：检测并关闭运行中的启动器 =====
function InitializeUninstall(): Boolean;
begin
  Result := True;

  if IsAppRunning() then
  begin
    if MsgBox('检测到白鹤服务器启动器正在运行。' #13#10#13#10 '卸载需要先关闭启动器。' #13#10 '是否立即关闭并继续卸载？', mbConfirmation, MB_YESNO) = IDYES then
    begin
      KillApp();
      Sleep(1000);
    end
    else
    begin
      MsgBox('请手动关闭白鹤服务器启动器后重新运行卸载程序。', mbInformation, MB_OK);
      Result := False;
    end;
  end;
end;
