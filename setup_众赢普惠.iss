; === FCU 氢燃料电池控制器上位机 安装包脚本（OEM 定制版：深圳市众赢普惠科技有限公司） ===
; 构建方式：与正式版同版本号，公司名不同。发布前需临时修改三处公司名（改回方式见 README「更新日志」V1.6.0 条目）：
;   1. MainWindow.xaml.cs  CustomerCompanyName 常量
;   2. MainWindow.xaml     tbk_ApplicationName 默认 Text
;   3. MainWindow.xaml     tbk_Copyright 默认 Text
; 然后执行：dotnet publish "FCU 氢燃料电池控/FCU 氢燃料电池控.csproj" -c Release -r win-x64 --self-contained -o publish_v1.9.3_zyph
; 最后 ISCC 本脚本。产物：Output\FCU氢燃料电池控制器_v1.9.3_众赢普惠.exe

#define MyAppName "FCU氢燃料电池控制器"
#define MyAppVersion "1.9.3"
#define MyAppPublisher "深圳市众赢普惠科技有限公司"
#define MyAppExeName "FCU 氢燃料电池控.exe"
#define MyAppCopyright "Copyright (C) 2026 深圳市众赢普惠科技有限公司"

[Setup]
; 与正式版同 AppId：同一台机器上交叉安装时按同一程序升级覆盖，
; 避免出现两个卸载项共用同一安装目录、卸载其一误删共享文件的问题
AppId={{B7E3F2A1-4D5C-6E8F-9A0B-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}

; 安装目录（与正式版同目录，升级覆盖安装）
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; 输出设置
OutputDir=Output
OutputBaseFilename=FCU氢燃料电池控制器_v{#MyAppVersion}_众赢普惠
Compression=lzma2/ultra64
SolidCompression=yes

; 权限与架构
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64bitMode=x64compatible

; 界面
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"

; === 安装文件 ===
[Files]
; 自包含发布：众赢普惠 OEM 版独立目录
Source: "publish_v1.9.3_zyph\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

; === 快捷方式 ===
[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; === 安装完成后 ===
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

; === 卸载清理 ===
[UninstallDelete]
Type: filesandordirs; Name: "{app}"
