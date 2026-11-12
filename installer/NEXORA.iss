; Inno Setup 6 — установщик NEXORA Launcher.
; Версию передаёт build\build.ps1: ISCC /DAppVersion=0.1.0 ...
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppName "NEXORA"
#define AppExe  "NEXORA.exe"

[Setup]
; AppId НЕЛЬЗЯ менять между версиями: по нему Windows понимает, что это обновление той же программы.
AppId={{6F1D2C7E-4B3A-4E59-9A0C-7D2E8B51C0A4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NEXORA
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Только Windows 10/11 x64
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Program Files требует прав администратора
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=NEXORA-Setup
SetupIconFile=..\assets\NEXORA.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} Launcher
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
; Обновление поверх запущенного лаунчера: закрыть и перезапустить его
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные ярлыки:"

[Files]
Source: "..\dist\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить NEXORA"; Flags: nowait postinstall skipifsilent

; Удаление: удаляется только папка программы. Настройки (%AppData%\NEXORA)
; и игровые файлы (%AppData%\.nexora) сохраняются, чтобы не потерять миры и моды.
