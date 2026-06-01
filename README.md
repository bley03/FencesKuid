# FencesWPF — Organizador de Escritorio Gratuito

Alternativa gratuita a Stardock Fences 6, construida con WPF/.NET 8.

---

## Requisitos

| Herramienta | Versión mínima |
|---|---|
| Visual Studio | 2022 (cualquier edición, incluye Community gratis) |
| .NET SDK | 8.0 o superior |
| Windows | 10 / 11 (64-bit) |

---

## Instalación y configuración en Visual Studio

### 1. Abrir el proyecto
1. Abre **Visual Studio 2022**.
2. Selecciona **Abrir una carpeta** → elige la carpeta `FencesWPF`.
3. O crea una nueva solución: **Archivo → Nuevo → Proyecto desde código existente**.

### 2. Verificar la plataforma
- En la barra de herramientas, asegúrate de tener **x64** seleccionado (no Any CPU).
- Esto es importante por las APIs nativas de Shell (SHGetFileInfo, etc.).

### 3. Instalar dependencias NuGet
Las dependencias se instalan automáticamente al compilar.  
Si no lo hacen, abre la **Consola del Administrador de paquetes** y ejecuta:

```
Install-Package Newtonsoft.Json -Version 13.0.3
```

### 4. Compilar y ejecutar
- Presiona **F5** para compilar y ejecutar en modo debug.
- O **Ctrl+F5** para ejecutar sin depurador.

---

## Estructura del proyecto

```
FencesWPF/
├── FencesWPF.csproj          ← Archivo de proyecto (.NET 8 WPF)
├── App.xaml / App.xaml.cs    ← Punto de entrada
├── Models/
│   └── FenceModels.cs        ← FenceData, AppSettings, enums
├── Services/
│   ├── StorageService.cs     ← Persistencia en %AppData%\FencesWPF\
│   └── FenceManager.cs       ← Singleton: gestiona todos los fences
└── Views/
    ├── FencePanel.*          ← Panel principal (drag & drop, iconos)
    ├── FenceSettingsDialog.* ← Configuración individual por fence
    ├── GlobalSettingsWindow.*← Configuración global de la app
    ├── RenameDialog.*        ← Dialog para renombrar (reemplaza VB InputBox)
    └── SearchDialog.*        ← Buscar accesos directos en todos los fences
```

---

## ¿Dónde se guardan los datos?

Los layouts y configuraciones se guardan en:

```
C:\Users\<TuUsuario>\AppData\Roaming\FencesWPF\
├── layout.json      ← Posición y contenido de todos los fences
├── settings.json    ← Configuración global
└── backups\         ← Últimos 5 backups automáticos
```

**Este era el bug principal del proyecto original**: se guardaba en el directorio del ejecutable (se perdía al mover/reinstalar). Ahora usa AppData correctamente.

---

## Cómo usar

### Crear un fence
- Clic derecho en el icono de la bandeja → **➕ Nuevo Fence**

### Agregar accesos directos
- Arrastra archivos, carpetas o accesos directos (`.lnk`) al interior del fence

### Renombrar un fence
- Doble clic sobre el título del fence

### Botones del fence
| Botón | Función |
|---|---|
| 🔓 / 🔒 | Bloquear / desbloquear movimiento |
| 📌 / 📍 | Anclar expandido (modo AutoRoll) |
| − / + | Colapsar / expandir |
| ⚙ | Configuración del fence |

### Configuración por fence
- Opacidad con slider
- Color de fondo, borde y título (código hex tipo `#CC1E1E2E` = alfa+RGB)
- Modo: Estático / Auto-Roll / Siempre colapsado
- Tamaño de iconos: Pequeño (32) / Mediano (48) / Grande (64)
- Temas rápidos: Oscuro, Azul, Verde, Rojo, Morado, Noche
- Eliminar fence

### Configuración global (doble clic en bandeja)
- Iniciar con Windows
- Guardado automático (intervalo configurable)
- Alineamiento magnético (snap) entre fences
- Modo e icono por defecto para nuevos fences
- Botón para abrir la carpeta de datos

### Búsqueda global
- Bandeja → **🔍 Buscar Acceso Directo**
- Busca en todos los fences por nombre o ruta
- Doble clic en el resultado para abrirlo

### Exportar / Importar layout
- Bandeja → **📤 Exportar Layout** — guarda una copia del layout en cualquier ubicación
- Bandeja → **📥 Importar Layout** — carga un layout guardado previamente

---

## Correcciones respecto al proyecto original

1. **Bug de persistencia resuelto** — datos en `%AppData%` no en el directorio del exe
2. **Conflicto de `SendMessage`** — firma única con `IntPtr` en todos los parámetros
3. **`SetParent(workerw)` eliminado** — rompía `DragMove()`. Reemplazado por `WS_EX_TOOLWINDOW` + push a `HWND_BOTTOM`
4. **`Microsoft.VisualBasic.InputBox` eliminado** — reemplazado por `RenameDialog` nativo WPF
5. **Estructura de proyecto completa** — `.csproj` SDK-style, carpetas separadas
6. **Guardado robusto** — auto-save timer, backup automático al importar

---

## Problemas comunes

**Los fences no aparecen detrás de las ventanas**  
→ Haz clic en el escritorio o minimiza las ventanas. Los fences están en la capa inferior (HWND_BOTTOM) y siempre quedan detrás de aplicaciones normales.

**Los iconos se ven grises (icono de pregunta)**  
→ La ruta del archivo no existe o fue movida. Elimina el acceso directo y vuelve a arrastrarlo.

**Error al compilar: SDK no encontrado**  
→ Instala el .NET 8 SDK desde https://dotnet.microsoft.com/download/dotnet/8.0
