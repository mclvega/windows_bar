# Genera el instalador de WindowsBar para otros PCs: dist\WindowsBar-Setup-<versión>.exe
# Incluye el runtime de .NET (el otro PC no necesita instalar nada).
# Requiere Inno Setup 6:  winget install --id JRSoftware.InnoSetup -e --scope user
# Uso:  powershell -ExecutionPolicy Bypass -File .\build-installer.ps1

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\WindowsBar\WindowsBar.csproj'
$files   = Join-Path $root 'publish\installer-files'
$iss     = Join-Path $root 'installer\WindowsBar.iss'

# 1. Versión desde el proyecto.
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No se encontró <Version> en $project." }
Write-Host "WindowsBar $version"

# 2. Publicación autocontenida (en carpeta: WPF da menos problemas que como archivo único).
if (Test-Path $files) { Remove-Item $files -Recurse -Force }
Write-Host 'Publicando (autocontenida, win-x64)...'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $files --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }

# 3. Compilador de Inno Setup (instalación por usuario de winget o en Program Files).
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'No se encontró Inno Setup 6. Instálalo con: winget install --id JRSoftware.InnoSetup -e --scope user' }

Write-Host 'Compilando el instalador...'
& $iscc "/DAppVersion=$version" /Q $iss
if ($LASTEXITCODE -ne 0) { throw 'La compilación del instalador falló.' }

$setup = Get-Item (Join-Path $root "dist\WindowsBar-Setup-$version.exe")
Write-Host ("Instalador listo: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
