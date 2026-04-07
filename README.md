# Barraca RRHH Windows v3

Versión funcional ampliada del sistema de gestión de RRHH, obras, horas, pagos y distribución de costos para Windows.

## Qué agrega esta v3
- gestión de períodos abiertos / cerrados
- reportes PDF con QuestPDF
- registro de incidencias de importación
- base WPF más completa con pestañas de operación
- script SQL actualizado
- distribución de costo RRHH basada en regla de 3 por horas equivalentes

## Stack
- .NET 8
- WPF (MVVM simple)
- SQL Server Express / SQL Server
- Entity Framework Core
- ClosedXML
- QuestPDF
- Serilog

## Estructura
- `src/Barraca.RRHH.Domain`: entidades y enums
- `src/Barraca.RRHH.Application`: interfaces y DTOs
- `src/Barraca.RRHH.Infrastructure`: EF Core, importaciones, distribución, reportes PDF y servicios
- `src/Barraca.RRHH.App`: aplicación WPF
- `sql/01_schema.sql`: esquema de base de datos inicial
- `docs/mapeo_plantillas_v3.md`: notas de mapeo de las planillas reales

## Nota importante
Este entorno no dispone del SDK de .NET, por lo que el proyecto se entrega armado y coherente, pero no compilado localmente aquí. La intención es que lo abras en Visual Studio 2022+, restaures paquetes NuGet y continúes sobre esta base.

## Chequeo general (abril 2026)
- Arquitectura por capas correcta: Domain, Application, Infrastructure y App.
- La app principal `Barraca.RRHH.App` es WPF (`net8.0-windows`), por eso solo corre en Windows.
- La cadena por defecto usa `LocalDB`, que es exclusivo de Windows.
- Se agregó una app nueva multiplataforma para macOS en `src/Barraca.RRHH.App.Mac` (Avalonia sobre .NET 8).

## Extensiones recomendadas para VS Code
Se agregó el archivo `.vscode/extensions.json` con recomendaciones para trabajar el proyecto:
- C# Dev Kit
- C#
- .NET Runtime
- Avalonia for VS Code
- SQL Server (mssql)
- GitLens

Al abrir la carpeta en VS Code te va a ofrecer instalarlas automáticamente.

## App para macOS
Se agregó el proyecto `Barraca.RRHH.App.Mac` que reutiliza servicios de Application/Infrastructure para:
- refrescar dashboard por período
- recalcular distribución
- generar reportes PDF

### Configuración previa
1. Instalar .NET SDK 8 en macOS.
2. Configurar cadena de conexión en `src/Barraca.RRHH.App.Mac/appsettings.json` (SQL Server accesible desde Mac).

### Ejecutar en desarrollo
1. `dotnet restore Barraca.RRHH.sln`
2. `dotnet run --project src/Barraca.RRHH.App.Mac/Barraca.RRHH.App.Mac.csproj`

### Publicar app macOS
1. Apple Silicon:
	`dotnet publish src/Barraca.RRHH.App.Mac/Barraca.RRHH.App.Mac.csproj -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true`
2. Intel:
	`dotnet publish src/Barraca.RRHH.App.Mac/Barraca.RRHH.App.Mac.csproj -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true`

### Publicar con VS Code Task
- Tasks disponibles en `.vscode/tasks.json`:
	- `restore-macos-project`
	- `publish-macos-arm64`
	- `package-macos-arm64`
	- `package-macos-x64`
- Salida esperada:
	- App bundle: `dist/macos-osx-arm64/Barraca RRHH.app`
	- Instalador: `dist/macos-osx-arm64/Barraca-RRHH-osx-arm64.dmg`

### Publicar con GitHub Actions (si tu Mac no puede descargar .NET)
- Workflow: `.github/workflows/build-macos.yml`
- Ejecuta `workflow_dispatch` desde GitHub y descarga el artefacto `barraca-rrhh-macos-arm64`.
- Ese artefacto incluye `.app` y `.dmg`.

## Nota de red detectada en este equipo
En este Mac hubo errores SSL al descargar el SDK desde `builds.dotnet.microsoft.com`, por eso no fue posible compilar localmente durante esta sesión. La alternativa inmediata es usar el workflow de GitHub Actions que ya quedó agregado.

## Flujo recomendado
1. Crear base en SQL Server.
2. Ejecutar `sql/01_schema.sql`.
3. Ajustar cadena de conexión en `src/Barraca.RRHH.App/appsettings.json`.
4. Abrir `Barraca.RRHH.sln`.
5. Restaurar paquetes NuGet.
6. Ejecutar la app.
7. Importar en este orden: Funcionarios, Obras, Horas, Pagos.
8. Calcular distribución.
9. Generar reportes PDF.

## Regla oficial de distribución
Para cada período y tipo de obra:
- `CostoTotalTipoObra = suma(TotalGenerado)` del tipo de obra
- `HorasTotalesTipoObra = suma(HorasEquivalentes)` del tipo de obra
- `MontoLinea = (HorasLinea / HorasTotalesTipoObra) * CostoTotalTipoObra`
- `ValorHora = MontoLinea / HorasLinea`
- `Jornales = HorasLinea / 8.8`

La suma de todas las líneas distribuidas de un tipo de obra debe coincidir con el costo total RRHH de ese tipo.
