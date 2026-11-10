; Device Check Agent — per-user installer (no administrator rights, no services, no drivers).
; Built by build/publish.ps1 -Installer (Inno Setup 6):
;   ISCC.exe /DAppVersion=0.2.0 /DSourceDir=..\dist\installer-root /DOutputDir=..\dist installer\DeviceCheckAgent.iss

#define AppName "Device Check Agent"
#define AppExe "DeviceCheck.Agent.exe"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\installer-root"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{8C4D2F7A-5E1B-4F3A-9C6D-2B7E1A0F4D93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Device Check
AppPublisherURL=https://device-check.umolab.ru
AppSupportURL=https://device-check.umolab.ru/agent
AppUpdatesURL=https://device-check.umolab.ru/agent
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Device Check Agent
DefaultGroupName=Device Check
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=device-check-agent-{#AppVersion}-setup
SetupIconFile=..\src\DeviceCheck.Agent.UI\Assets\agent.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; The tray app holds this mutex (Local\DeviceCheck.Agent): setup and uninstall ask to close it first.
AppMutex=DeviceCheck.Agent
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.DesktopIcon=Создать значок на рабочем столе
en.DesktopIcon=Create a desktop icon
ru.AutoStart=Запускать агента при входе в Windows (в трее, без открытия сайта)
en.AutoStart=Start the agent when I sign in to Windows (tray only, without opening the site)
ru.LaunchNow=Запустить Device Check Agent и открыть сайт
en.LaunchNow=Launch Device Check Agent and open the site

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeviceCheckAgent"; \
  ValueData: """{app}\{#AppExe}"" --no-open"; Tasks: autostart; Flags: uninsdeletevalue
; Unchecked on reinstall → remove the previous autostart entry.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DeviceCheckAgent"; \
  Flags: deletevalue; Tasks: not autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
