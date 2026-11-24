# Estado de plataformas

## Base

`Oracles-HD` parte de **Links-Awakening-DX-HD v2.0.6** (`2e68849`).

## Situación real del código heredado

El árbol de fuentes de v2.0.6 es **solo Windows**:

- `TargetFramework`: `net6.0-windows` (migrado a `net8.0-windows` en la Fase 1).
- Backend gráfico: `MonoGame.Framework.WindowsDX 3.8.1.303` (**DirectX 11**).
- `UseWindowsForms=true` (diálogos del editor, `MessageBox`, gestión de ventana).
- No hay paquetes, carpetas ni `#if` para Linux, macOS, OpenGL, Vulkan o DX12.

Verificado en el historial completo del repositorio: los tres commits existentes
(inicial, `origin/upstream` y v2.0.6) declaran el mismo `TargetFramework` y el
mismo paquete `WindowsDX`. La cadena `KNI`/`Vulkan`/`DX12` aparece únicamente
dentro de los `.exe` precompilados que vienen en el repositorio, no en el código.

## Estado actual

| Plataforma | Backend | Estado |
|---|---|---|
| Windows x64 | DirectX 11 (WindowsDX) | **Funcional** (Fase 1) |
| Linux x64 | OpenGL (DesktopGL) | **WIP** — pendiente de fuentes/port |
| macOS (Intel/ARM) | OpenGL/Metal (DesktopGL) | **WIP** — pendiente de fuentes/port |
| Windows | Vulkan | **WIP** — pendiente de fuentes/port |
| Windows | DirectX 12 | **WIP** — pendiente de fuentes/port |

No se ha añadido ninguna dependencia especulativa: el `.csproj` conserva
`MonoGame.Framework.WindowsDX` para no romper la única compilación verificada.

## Plan cuando lleguen las fuentes multiplataforma

1. Añadir condiciones por plataforma en `Oracles-HD.csproj`:
   `net8.0-windows` + `MonoGame.Framework.WindowsDX` para DX11,
   `net8.0` + `MonoGame.Framework.DesktopGL` para Linux/macOS,
   y los paquetes KNI/Vulkan/DX12 que correspondan a las fuentes recibidas.
2. Aislar las dependencias de WinForms que hoy son incondicionales
   (`Program.cs` usa `MessageBox` sin `#if WINDOWS`; el resto ya está guardado).
3. Compilar el contenido (`.fx` → MGFX) por plataforma; hoy `Content/Content.mgfx`
   se genera con el pipeline de Windows.
4. Verificar arranque y renderizado en cada backend antes de dar por cerrada la
   matriz.

## Nota sobre el binario oficial

El `.exe` oficial de la v2.0.6 contiene referencias a KNI, Vulkan, DX12,
WindowsDX y OpenGL, lo que sugiere que fue compilado desde un árbol distinto
(probablemente KNI) que no está incluido en este repositorio. No es posible
reconstruirlo desde estas fuentes sin ese árbol.
