#define MyAppName "ynoez"
#define MyAppPublisher "ynoez"
#define MyAppURL "https://github.com/6zyloov/ynoez-eternal-debloat"

[Setup]
AppId={{A79D82E6-81F5-4A61-9DB2-5CBF90E08F44}
AppName={#MyAppName}
AppVersion=__APP_VERSION__
AppVerName={#MyAppName} __APP_VERSION__
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName=C:\ynoez
DisableDirPage=yes
DefaultGroupName=ynoez
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=ynoez-setup-__APP_VERSION__
SetupLogging=yes
Uninstallable=yes
UninstallDisplayName=ynoez
UninstallDisplayIcon={app}\ynoez.exe
VersionInfoVersion=__APP_VERSION_QUAD__
VersionInfoProductName=ynoez
VersionInfoCompany=ynoez
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Dirs]
Name: "{app}\Data"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\Backups"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\Logs"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\Downloads"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\Toolbox\Debloat"
Name: "{app}\Toolbox\Internet"
Name: "{app}\Toolbox\Windows"
Name: "{app}\Toolbox\Servicos"
Name: "{app}\Toolbox\Drivers"
Name: "{app}\Toolbox\GPU"
Name: "{app}\Toolbox\CPU"
Name: "{app}\Toolbox\RAM"
Name: "{app}\Toolbox\Energia"
Name: "{app}\Toolbox\Jogos"
Name: "{app}\Toolbox\Brave"
Name: "{app}\Toolbox\Discord"
Name: "{app}\Toolbox\Ferramentas"
Name: "{app}\Toolbox\Diagnostico"
Name: "{app}\Toolbox\Restauracao"
Name: "{app}\Toolbox\Placas-mae"
Name: "{app}\Toolbox\Performance"; Permissions: users-modify

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ynoez"; Filename: "{app}\ynoez.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\ynoez"; Filename: "{app}\ynoez.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ynoez.exe"; Description: "Abrir ynoez"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\Toolbox\Performance\PresentMon.exe"
Type: files; Name: "{app}\Toolbox\Performance\PresentMon.exe.download"
