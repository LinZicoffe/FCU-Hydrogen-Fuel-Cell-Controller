; === FCU 氢燃料电池控制器上位机 安装包脚本 ===

#define MyAppName "FCU氢燃料电池控制器"
#define MyAppVersion "1.9.1"
#define MyAppPublisher "瑞达新能源（广州）有限公司"
#define MyAppExeName "FCU 氢燃料电池控.exe"
#define MyAppCopyright "Copyright (C) 2026 瑞达新能源（广州）有限公司"

[Setup]
AppId={{B7E3F2A1-4D5C-6E8F-9A0B-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright={#MyAppCopyright}

; 安装目录
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; 输出设置
OutputDir=Output
OutputBaseFilename=FCU氢燃料电池控制器_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

; 权限与架构
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 界面
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

; 许可协议（可选，取消注释启用）
; LicenseFile=LICENSE.txt

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"

; === 安装文件 ===
[Files]
; 自包含发布：使用 v1.9.1 独立目录，避免混入旧版文件
Source: "publish_v1.9.1\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

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
