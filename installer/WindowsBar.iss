; Instalador de WindowsBar (Inno Setup 6).
; No se compila a mano: lo hace build-installer.ps1, que antes publica la app autocontenida
; en publish\installer-files y pasa la versión con /DAppVersion=x.y.z.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; GUID fijo: las nuevas versiones sustituyen a la instalada.
AppId={{DA9118BE-C9B6-4CE8-8C88-B287E831D11B}
AppName=WindowsBar
AppVersion={#AppVersion}
AppVerName=WindowsBar {#AppVersion}
AppPublisher=WindowsBar
; Por usuario y sin administrador, en la misma carpeta que usa publish.ps1.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\WindowsBar
DisableDirPage=yes
DefaultGroupName=WindowsBar
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\src\WindowsBar\Resources\WindowsBar.ico
UninstallDisplayIcon={app}\WindowsBar.exe
UninstallDisplayName=WindowsBar
OutputDir=..\dist
OutputBaseFilename=WindowsBar-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; El cierre de la instancia en marcha lo hace [Code] (cierre limpio que devuelve la barra de Windows).
CloseApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked

[Files]
Source: "..\publish\installer-files\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WindowsBar"; Filename: "{app}\WindowsBar.exe"
Name: "{userdesktop}\WindowsBar"; Filename: "{app}\WindowsBar.exe"; Tasks: desktopicon

[Registry]
; Inicio con Windows (la app lo mantiene según el interruptor "Iniciar con Windows").
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WindowsBar"; ValueData: """{app}\WindowsBar.exe"""; Flags: uninsdeletevalue

[Run]
; A través de Explorer, para que herede el entorno normal del usuario (como publish.ps1).
Filename: "{win}\explorer.exe"; Parameters: """{app}\WindowsBar.exe"""; Description: "Iniciar WindowsBar"; Flags: nowait postinstall skipifsilent

[Code]
{ Cierra la instancia en marcha: primero de forma limpia (--exit, que devuelve la barra de Windows);
  si no termina en 10 s se fuerza el cierre y se restaura la barra nativa con --restore. }
procedure CloseWindowsBar();
var
  ResultCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\WindowsBar.exe');
  Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "' +
    '$p = Get-Process WindowsBar -ErrorAction SilentlyContinue; ' +
    'if (-not $p) { exit 0 }; ' +
    '$exe = ''' + Exe + '''; if (-not (Test-Path $exe)) { $exe = $p[0].Path }; ' +
    '& $exe --exit; ' +
    '$forced = $false; ' +
    '$p | ForEach-Object { if (-not $_.WaitForExit(10000)) { $_.Kill(); $_.WaitForExit(); $forced = $true } }; ' +
    'if ($forced) { & $exe --restore }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseWindowsBar();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CloseWindowsBar();
end;
