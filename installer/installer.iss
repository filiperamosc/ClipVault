[Setup]
AppId={{D41A28A4-5B8B-4E38-A392-58A28E5846E2}
AppName=ClipVault
AppVersion=1.0.0
AppPublisher=Filipe Ramos
AppPublisherURL=https://github.com/filiperamosc/ClipVault
AppSupportURL=https://github.com/filiperamosc/ClipVault/issues
AppUpdatesURL=https://github.com/filiperamosc/ClipVault/releases
DefaultDirName={autopf}\ClipVault
DefaultGroupName=ClipVault
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=..\bin\Installer
OutputBaseFilename=ClipVault_Setup_v1.0.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Iniciar o ClipVault automaticamente com o Windows (Bandeja do Sistema)"; GroupDescription: "Inicialização:"

[Files]
; Publicação autocontida gerada pelo dotnet publish
Source: "..\bin\Release\net8.0-windows10.0.19041.0\win10-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ClipVault"; Filename: "{app}\ClipVault.exe"
Name: "{group}\Desinstalar ClipVault"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ClipVault"; Filename: "{app}\ClipVault.exe"; Tasks: desktopicon

[Registry]
; Inicialização automática com o Windows na inicialização do usuário (sem exigir privilégios de Admin)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClipVault"; ValueData: """{app}\ClipVault.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\ClipVault.exe"; Description: "{cm:LaunchProgram,ClipVault}"; Flags: nowait postinstall skipifsilent
