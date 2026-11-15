# Compila WindowsBar en Release, la instala en %LOCALAPPDATA%\Programs\WindowsBar,
# la registra para iniciarse con Windows y la arranca.
# Uso:  powershell -ExecutionPolicy Bypass -File .\publish.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:LOCALAPPDATA 'Programs\WindowsBar'
$exe  = Join-Path $dest 'WindowsBar.exe'

# 1. Cerrar la instancia en marcha (limpiamente si es posible).
$running = Get-Process WindowsBar -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Cerrando WindowsBar en ejecución...'
    $closer = if (Test-Path $exe) { $exe } else { $running[0].Path }
    & $closer --exit
    $running | ForEach-Object { if (-not $_.WaitForExit(10000)) { $_.Kill(); $_.WaitForExit() } }
    # Si hubo que forzar el cierre, devolvemos la barra nativa a su estado original.
    & $closer --restore
}

# 2. Compilar e instalar.
Write-Host "Publicando en $dest ..."
dotnet publish (Join-Path $root 'src\WindowsBar\WindowsBar.csproj') -c Release -o $dest --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }

# 3. Inicio con Windows (la app también lo comprueba al arrancar).
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'WindowsBar' -Value "`"$exe`""

# 4. Arrancar.
Start-Process $exe
Write-Host 'WindowsBar instalada, registrada en el inicio de Windows y en ejecución.'
