# WindowsBar

Barra de tareas alternativa para Windows 11, hecha en WPF (.NET 6). Reemplaza a la barra nativa de Explorer
manteniendo su aspecto y comportamiento: Inicio, apps ancladas y abiertas, bandeja del sistema, reloj y
accesos rápidos, con soporte para varios monitores.

Al arrancar oculta la barra de Windows y guarda su estado original; al salir (o con `--restore`) la devuelve
tal como estaba.

## Funciones

- **Inicio, Búsqueda, Vista de tareas y Widgets**, que abren los paneles propios de Windows en el monitor
  donde se hace clic.
- **Apps ancladas y ventanas abiertas** agrupadas por aplicación. Clic derecho sobre una app para anclarla,
  desanclarla, moverla, cerrar sus ventanas o finalizar la tarea.
- **Bandeja del sistema** con panel `^` de iconos ocultos; cada icono se puede mostrar u ocultar.
- **Iconos del sistema**: red, volumen, batería, idioma de entrada y aviso de micrófono en uso.
- **Reloj** con fecha (opcionalmente con segundos); al pulsarlo abre el centro de notificaciones.
- **Mostrar escritorio**, en la franja del extremo derecho.
- **Varios monitores**: una barra por monitor, con todas las ventanas o solo las de ese monitor.
- **Ocultar automáticamente** y alineación **centrada** (Windows 11) o a la izquierda.
- **Inicio con Windows** mediante `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Requisitos

- Windows 11 (o Windows 10 con Explorer como shell).
- [SDK de .NET 6](https://dotnet.microsoft.com/download/dotnet/6.0) o posterior para compilar.

Dependencias NuGet: [ManagedShell](https://github.com/cairoshell/ManagedShell) y
[NAudio.Wasapi](https://github.com/naudio/NAudio).

## Instalación

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

El script:

1. Cierra limpiamente la instancia en marcha, si la hay.
2. Compila en Release y la instala en `%LOCALAPPDATA%\Programs\WindowsBar`.
3. La registra para iniciarse con Windows.
4. La arranca a través de Explorer, para que herede el entorno normal del usuario.

El mismo comando sirve para actualizar una instalación existente.

### Instalador para otro PC

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

Genera `dist\WindowsBar-Setup-<versión>.exe`, un único archivo que incluye el runtime de .NET: el otro PC no
necesita instalar nada más. Requiere [Inno Setup 6](https://jrsoftware.org/isinfo.php) en el PC donde se
genera (`winget install --id JRSoftware.InnoSetup -e --scope user`).

El instalador:

- Instala por usuario, sin permisos de administrador, en `%LOCALAPPDATA%\Programs\WindowsBar`.
- Crea el acceso en el menú Inicio (y, si se marca, en el escritorio) y la registra para iniciarse con Windows.
- Cierra limpiamente la instancia en marcha antes de actualizar.
- Deja un desinstalador en *Configuración → Aplicaciones → Aplicaciones instaladas*, que también devuelve la
  barra de Windows. La configuración del usuario (`%APPDATA%\WindowsBar`) se conserva.

El `.exe` no está firmado: en el otro PC, SmartScreen mostrará un aviso; pulsa **Más información →
Ejecutar de todas formas**.

## Desarrollo

```powershell
dotnet build
dotnet run --project src\WindowsBar
```

Las ejecuciones desde una carpeta `bin\` se consideran de desarrollo y **no** se registran en el inicio de
Windows.

Para regenerar el icono de la app (`src\WindowsBar\Resources\WindowsBar.ico`):

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\make-icon.ps1
```

## Uso

- **Clic derecho en una zona vacía de la barra**: Administrador de tareas, *Cambiar el orden de las apps
  arrastrando* y Configuración de WindowsBar.
- **Reordenar apps**: con *Cambiar el orden de las apps arrastrando* activado, arrastra los iconos de la barra
  para moverlos (las ancladas guardan su orden).
- **Vistas previas**: al pasar el ratón sobre una app abierta se muestran las miniaturas de sus ventanas; al
  pasar sobre una miniatura se ve esa ventana en el escritorio y con clic se trae al frente.
- **Configuración de WindowsBar**: secciones General, Elementos de la barra, Bandeja del sistema y Apps
  ancladas. Las apps ancladas de la barra de Windows se importan la primera vez y se pueden volver a importar
  desde ahí.

### Línea de comandos

| Comando                     | Efecto                                                                 |
| --------------------------- | ---------------------------------------------------------------------- |
| `WindowsBar.exe`            | Inicia la barra (solo se permite una instancia).                      |
| `WindowsBar.exe --exit`     | Pide a la instancia en marcha que se cierre limpiamente.              |
| `WindowsBar.exe --restore`  | Devuelve la barra nativa a su estado original (tras un cierre forzado). |

## Archivos de la app

| Ruta                                         | Contenido                                        |
| -------------------------------------------- | ------------------------------------------------ |
| `%APPDATA%\WindowsBar\settings.json`         | Configuración, apps ancladas y visibilidad de la bandeja. |
| `%LOCALAPPDATA%\WindowsBar\diag.log`         | Registro de diagnóstico (se recorta al pasar de 1 MB). |
| `%LOCALAPPDATA%\Programs\WindowsBar\`        | Instalación creada por `publish.ps1` o por el instalador. |

## Solución de problemas

**La barra de Windows quedó oculta** tras cerrar WindowsBar de forma forzada:

```powershell
& "$env:LOCALAPPDATA\Programs\WindowsBar\WindowsBar.exe" --restore
```

**Desinstalar**: salir de WindowsBar (clic derecho en la barra → *Salir de WindowsBar*), borrar el valor
`WindowsBar` de `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` y eliminar las carpetas indicadas en
[Archivos de la app](#archivos-de-la-app).

## Estructura

```
src/WindowsBar/
├── Controls/    Elementos de la barra (Inicio, tareas, bandeja, reloj, iconos del sistema...)
├── Interop/     Llamadas Win32 y control de la barra nativa
├── Services/    Lógica: ventanas, ajustes, audio, red, energía, apps ancladas, inicio con Windows
├── Themes/      Estilos (Win11Dark.xaml)
├── Views/       TaskbarWindow (la barra) y SettingsWindow (configuración)
└── Program.cs   Punto de entrada: instancia única y argumentos de línea de comandos
publish.ps1      Compila, instala y registra la app
tools/           Utilidades (generación del icono)
```
