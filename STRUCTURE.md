# AI GIS Converter — Solution Structure (Module 0)

Production-ready CAD → GIS conversion desktop application.
**C# .NET 8 · WPF (MVVM) · Clean Architecture · SOLID · DI · async-first**

---

## 1. Layer map

| # | Layer | Project | TFM | May reference |
|---|-------|---------|-----|---------------|
| 1 | Presentation | `AiGisConverter.Presentation` | `net8.0-windows` | Everything (composition root only) |
| 2 | Application | `AiGisConverter.Application` | `net8.0` | Domain |
| 3 | Business | `AiGisConverter.Business` | `net8.0` | Domain |
| 4 | Data | `AiGisConverter.Data` | `net8.0` | Domain |
| 5 | Infrastructure | `AiGisConverter.Infrastructure` | `net8.0` | Domain, Application |
| 6 | AI | `AiGisConverter.Ai` | `net8.0` | Domain |
| 7 | GIS | `AiGisConverter.Gis` | `net8.0` | Domain |
| 8 | CAD | `AiGisConverter.Cad` | `net8.0` | Domain |
| 9 | QA/QC | `AiGisConverter.QaQc` | `net8.0` | Domain |
| — | Domain | `AiGisConverter.Domain` | `net8.0` | *nothing* |

### Dependency rule

```
                    ┌──────────────────────────┐
                    │       Presentation       │  WPF · MVVM · composition root
                    └────────────┬─────────────┘
                                 │
                    ┌────────────▼─────────────┐
                    │       Application        │  use cases · pipeline · batch
                    └────────────┬─────────────┘
                                 │
        ┌───────────┬────────────┼────────────┬───────────┬───────────┐
        │           │            │            │           │           │
   ┌────▼────┐ ┌────▼────┐  ┌────▼────┐  ┌────▼────┐ ┌────▼────┐ ┌───▼────────┐
   │   CAD   │ │   GIS   │  │   AI    │  │  QA/QC  │ │  Data   │ │Infrastructure│
   └────┬────┘ └────┬────┘  └────┬────┘  └────┬────┘ └────┬────┘ └───┬────────┘
        └───────────┴────────────┼────────────┴───────────┴──────────┘
                                 │
                    ┌────────────▼─────────────┐
                    │          Domain          │  entities · value objects · PORTS
                    └──────────────────────────┘
```

**Every arrow points inward.** All ports (`ICadReader`, `IGeometryConverter`,
`ICrsDetector`, `IAiClassifier`, `IQaQcEngine`, `IFeatureExporter`, `IUnitOfWork`,
`IRepository<T>`, …) are declared in `Domain/Abstractions`. Outer layers implement
them; nothing inner ever references outward. Dependency Inversion is structural,
not conventional.

---

## 2. Full folder tree

```
AiGisConverter/
├── assets/
│   └── models/
│       └── onnx/
├── build/
│   └── build.ps1
├── docs/
│   ├── adr/
│   ├── architecture/
│   └── user-guide/
├── samples/
├── src/
│   ├── AiGisConverter.Ai/
│   │   ├── Abstractions/
│   │   ├── DependencyInjection/
│   │   ├── Factories/
│   │   ├── Features/
│   │   ├── Models/
│   │   ├── Options/
│   │   ├── Prompting/
│   │   ├── Providers/
│   │   │   ├── Ollama/
│   │   │   ├── Onnx/
│   │   │   ├── OpenAi/
│   │   │   └── RuleBased/
│   │   └── AiGisConverter.Ai.csproj
│   ├── AiGisConverter.Application/
│   │   ├── Abstractions/
│   │   ├── DependencyInjection/
│   │   ├── Dtos/
│   │   │   ├── Ai/
│   │   │   ├── Cad/
│   │   │   ├── Gis/
│   │   │   └── QaQc/
│   │   ├── Mapping/
│   │   ├── Pipelines/
│   │   │   └── Steps/
│   │   ├── Progress/
│   │   ├── Services/
│   │   │   ├── Batch/
│   │   │   ├── Conversion/
│   │   │   └── Project/
│   │   ├── Validation/
│   │   └── AiGisConverter.Application.csproj
│   ├── AiGisConverter.Business/
│   │   ├── Abstractions/
│   │   ├── DependencyInjection/
│   │   ├── Policies/
│   │   ├── Rules/
│   │   │   ├── Classification/
│   │   │   └── Mapping/
│   │   ├── Services/
│   │   └── AiGisConverter.Business.csproj
│   ├── AiGisConverter.Cad/
│   │   ├── Abstractions/
│   │   ├── DependencyInjection/
│   │   ├── Extractors/
│   │   ├── Factories/
│   │   ├── Models/
│   │   ├── Options/
│   │   ├── Providers/
│   │   │   ├── AutoCad/
│   │   │   └── Dxf/
│   │   └── AiGisConverter.Cad.csproj
│   ├── AiGisConverter.Data/
│   │   ├── Configurations/
│   │   ├── Context/
│   │   ├── DependencyInjection/
│   │   ├── Migrations/
│   │   ├── Options/
│   │   ├── Repositories/
│   │   ├── Seed/
│   │   ├── UnitOfWork/
│   │   └── AiGisConverter.Data.csproj
│   ├── AiGisConverter.Domain/
│   │   ├── Abstractions/
│   │   │   ├── Repositories/
│   │   │   └── Services/
│   │   ├── Common/
│   │   ├── Entities/
│   │   │   ├── Cad/
│   │   │   ├── Gis/
│   │   │   ├── Project/
│   │   │   └── QaQc/
│   │   ├── Enums/
│   │   ├── Events/
│   │   ├── Exceptions/
│   │   ├── Specifications/
│   │   ├── ValueObjects/
│   │   └── AiGisConverter.Domain.csproj
│   ├── AiGisConverter.Gis/
│   │   ├── Abstractions/
│   │   ├── Crs/
│   │   ├── DependencyInjection/
│   │   ├── Exporters/
│   │   │   ├── Csv/
│   │   │   ├── GeoJson/
│   │   │   ├── GeoPackage/
│   │   │   ├── Kml/
│   │   │   └── Shapefile/
│   │   ├── Factories/
│   │   ├── Gdal/
│   │   ├── Geometry/
│   │   ├── Options/
│   │   ├── Projection/
│   │   └── AiGisConverter.Gis.csproj
│   ├── AiGisConverter.Infrastructure/
│   │   ├── Caching/
│   │   ├── Configuration/
│   │   ├── DependencyInjection/
│   │   ├── FileSystem/
│   │   ├── Http/
│   │   ├── Logging/
│   │   ├── Security/
│   │   ├── Threading/
│   │   └── AiGisConverter.Infrastructure.csproj
│   ├── AiGisConverter.Presentation/
│   │   ├── Assets/
│   │   │   ├── Icons/
│   │   │   └── Images/
│   │   ├── Behaviors/
│   │   ├── Controls/
│   │   ├── Converters/
│   │   ├── Dialogs/
│   │   ├── Mvvm/
│   │   ├── Resources/
│   │   │   ├── Styles/
│   │   │   └── Themes/
│   │   ├── Services/
│   │   ├── Startup/
│   │   ├── ViewModels/
│   │   │   ├── Ai/
│   │   │   ├── Batch/
│   │   │   ├── Cad/
│   │   │   ├── Gis/
│   │   │   ├── QaQc/
│   │   │   ├── Settings/
│   │   │   └── Shell/
│   │   ├── Views/
│   │   │   ├── Ai/
│   │   │   ├── Batch/
│   │   │   ├── Cad/
│   │   │   ├── Gis/
│   │   │   ├── QaQc/
│   │   │   ├── Settings/
│   │   │   └── Shell/
│   │   ├── AiGisConverter.Presentation.csproj
│   │   ├── appsettings.Development.json
│   │   └── appsettings.json
│   └── AiGisConverter.QaQc/
│       ├── Abstractions/
│       ├── DependencyInjection/
│       ├── Engine/
│       ├── Options/
│       ├── Reporting/
│       ├── Rules/
│       │   ├── Attribute/
│       │   ├── Crs/
│       │   ├── Geometry/
│       │   └── Topology/
│       └── AiGisConverter.QaQc.csproj
├── tests/
│   ├── AiGisConverter.Ai.Tests/
│   │   └── AiGisConverter.Ai.Tests.csproj
│   ├── AiGisConverter.Application.Tests/
│   │   └── AiGisConverter.Application.Tests.csproj
│   ├── AiGisConverter.Business.Tests/
│   │   └── AiGisConverter.Business.Tests.csproj
│   ├── AiGisConverter.Cad.Tests/
│   │   └── AiGisConverter.Cad.Tests.csproj
│   ├── AiGisConverter.Data.Tests/
│   │   └── AiGisConverter.Data.Tests.csproj
│   ├── AiGisConverter.Domain.Tests/
│   │   └── AiGisConverter.Domain.Tests.csproj
│   ├── AiGisConverter.Gis.Tests/
│   │   └── AiGisConverter.Gis.Tests.csproj
│   ├── AiGisConverter.Infrastructure.Tests/
│   │   └── AiGisConverter.Infrastructure.Tests.csproj
│   ├── AiGisConverter.IntegrationTests/
│   │   └── AiGisConverter.IntegrationTests.csproj
│   ├── AiGisConverter.Presentation.Tests/
│   │   └── AiGisConverter.Presentation.Tests.csproj
│   ├── AiGisConverter.QaQc.Tests/
│   │   └── AiGisConverter.QaQc.Tests.csproj
│   ├── TestData/
│   │   ├── Dwg/
│   │   ├── Dxf/
│   │   └── Expected/
│   └── Directory.Build.props
├── .editorconfig
├── .gitignore
├── AiGisConverter.sln
├── Directory.Build.props
├── Directory.Packages.props
└── nuget.config
```

---

## 3. Layer responsibilities

### Domain — `src/AiGisConverter.Domain`
Zero-dependency core (NetTopologySuite only, as a geometry value library).

- `Entities/Cad` — `CadDocument`, `CadLayer`, `CadEntity`, `CadBlockReference`, `CadTextEntity`
- `Entities/Gis` — `GisFeature`, `GisFeatureCollection`, `GisAttributeSchema`
- `Entities/Project` — `ConversionProject`, `ConversionJob`, `ConversionRun`
- `Entities/QaQc` — `ValidationIssue`, `ValidationReport`
- `ValueObjects` — `CoordinateSystem`, `Extent`, `LayerName`, `AttributeValue`, `Confidence`
- `Enums` — `CadEntityType`, `GeometryKind`, `ExportFormat`, `AiProviderKind`, `IssueSeverity`
- `Common` — `Result`, `Result<T>`, `Error`, `Entity`, `ValueObject`, `IAggregateRoot`
- `Abstractions/Services` — the **ports** every outer layer implements
- `Abstractions/Repositories` — `IRepository<T>`, `IProjectRepository`, `IUnitOfWork`
- `Exceptions` — `DomainException` hierarchy
- `Specifications` — reusable query specifications

### Application — `src/AiGisConverter.Application`
Orchestration, no I/O of its own.

- `Pipelines/Steps` — Read → Detect CRS → Convert Geometry → Classify → AI Classify → QA/QC → Export
- `Services/Conversion`, `Services/Batch`, `Services/Project`
- `Dtos`, `Mapping`, `Validation`, `Progress` (`IProgress<ConversionProgress>`)
- `DependencyInjection/ApplicationServiceCollectionExtensions.cs`

### Business — `src/AiGisConverter.Business`
Deterministic domain rules that are *not* use-case orchestration: CAD-layer →
GIS-feature-class mapping tables, naming policies, attribute derivation,
classification rule sets that the AI layer can defer to or override.

### CAD — `src/AiGisConverter.Cad`
- `Providers/Dxf` — netDxf. Always compiled. DXF read, no external SDK.
- `Providers/AutoCad` — Autodesk .NET API (RealDWG / accoreconsole) for DWG.
  **Excluded from compilation by default.** Enable with
  `-p:EnableAutoCadProvider=true -p:AutoCadSdkPath=<ObjectARX inc folder>`.
- `Factories/CadReaderFactory` — resolves provider by extension + availability probe.
- `Extractors` — one extractor per entity kind: layers, blocks, text, polylines,
  lines, arcs, circles, hatches, coordinates.

### GIS — `src/AiGisConverter.Gis`
- `Geometry` — CAD primitive → NTS geometry (arc/circle tessellation, hatch → polygon)
- `Crs` — detection chain: `.prj` sidecar → DWG GeoData → ESRI XData → extent heuristic → user
- `Projection` — ProjNet/PROJ transform pipeline, cached
- `Exporters` — Shapefile, GeoJSON, GeoPackage, CSV, KML (one folder each, `IFeatureExporter`)
- `Gdal` — one-time native GDAL/PROJ bootstrap (`MaxRev.Gdal.Core`)

### AI — `src/AiGisConverter.Ai`
Four interchangeable `IAiClassifier` implementations behind `AiClassifierFactory`:
`Onnx` (offline), `OpenAi` (cloud), `Ollama` (local LLM), `RuleBased` (deterministic
fallback so the app is fully functional with no model configured).
`Features` holds the feature-extraction that turns a CAD layer into model input.

### QA/QC — `src/AiGisConverter.QaQc`
`Engine` runs an ordered `IValidationRule` set: `Rules/Geometry` (self-intersection,
zero length/area, duplicate vertices, unclosed polygons), `Rules/Attribute`
(nulls, domain violations, field-length for Shapefile), `Rules/Topology`
(dangles, overlaps, gaps), `Rules/Crs` (missing/mismatched CRS, out-of-range coords).
`Reporting` emits HTML and CSV reports.

### Data — `src/AiGisConverter.Data`
EF Core + SQLite. `Context`, `Configurations` (fluent `IEntityTypeConfiguration<T>`),
`Repositories` (generic + specific), `UnitOfWork`, `Migrations`, `Seed`.

### Infrastructure — `src/AiGisConverter.Infrastructure`
Cross-cutting only: Serilog setup, `IConfiguration` binding, `IFileSystem`,
typed `HttpClient` factory + Polly-style retry, caching, `IClock`,
`IBackgroundTaskQueue`, secret resolution for API keys.

### Presentation — `src/AiGisConverter.Presentation`
WPF, MVVM, `CommunityToolkit.Mvvm`. `Startup` is the **only** place where all
layers meet: `App.xaml.cs` builds the `IHost`, calls each layer's
`Add<Layer>()` extension, resolves `ShellWindow`.

---

## 4. Root files

| File | Purpose |
|------|---------|
| `AiGisConverter.sln` | 21 projects, `Debug\|x64` / `Release\|x64` |
| `Directory.Build.props` | C# 12, nullable, warnings-as-errors, XML docs on |
| `Directory.Packages.props` | Central Package Management — every version in one place |
| `nuget.config` | Pinned to nuget.org |
| `.editorconfig` | Microsoft C# conventions + naming rules as build errors |
| `build/build.ps1` | restore → build → test + coverage → publish |
| `src/…/appsettings.json` | Full configuration surface for all nine layers |

---

## 5. Build

```powershell
# Default — DXF only, no AutoCAD SDK required
dotnet build AiGisConverter.sln -c Release

# With the AutoCAD DWG provider
.\build\build.ps1 -Configuration Release `
    -EnableAutoCadProvider -AutoCadSdkPath 'C:\ObjectARX 2025\inc'
```

---

## 6. Module plan

| Module | Content | Status |
|--------|---------|--------|
| 0 | Solution + folder structure + build config | **done** |
| 1 | Domain layer | **done** |
| 2 | CAD layer | **done** |
| 3 | GIS layer | **done** |
| 4 | AI layer | **done** |
| 4.5 | Plugin SDK, host, bridge, 12 plugin categories | **done** |
| 4.8 | Spatial Processing Engine | **done** |
| 5 | QA/QC layer | **done** |
| 6 | Data + Infrastructure layers | **done** |
| 7 | Application layer | **done** |
| 8 | Presentation layer (WPF MVVM) | **done** |
| 9 | Tests + verification | pending |

| Module 9 | Tests, Verification & Production Readiness | **done** — see `docs/MODULE-9-FINAL-REPORT.md` |
