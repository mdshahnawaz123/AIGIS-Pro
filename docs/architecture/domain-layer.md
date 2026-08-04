# Domain layer

`src/AiGisConverter.Domain` — 75 files, **one** package reference (NetTopologySuite), **zero**
project references. It compiles on a machine with no CAD SDK, no GDAL and no network.

## Contents

| Folder | Files | What lives here |
|--------|-------|-----------------|
| `Common` | 7 | `Entity<TId>`, `Result`/`Result<T>`, `Error`, `IAggregateRoot`, `DomainEvent`, typed ids |
| `ValueObjects` | 8 | `CoordinateSystem`, `Extent`, `LayerName`, `FeatureClass`, `AttributeValue`, `FieldDefinition`, `Confidence`, `ConversionSettings` |
| `Enums` | 9 | `GeometryKind`, `CadEntityType`, `ExportFormat`, `IssueSeverity`, `IssueCategory`, `ConversionStatus`, `CrsDetectionSource`, `AttributeDataType`, `LinearUnit` |
| `Entities/Source` | 4 | `SourceDocument`, `SourceLayer`, `SourceElement`, `SourceReference` |
| `Entities/Gis` | 3 | `GisDataset`, `GisFeature`, `GisAttributeSchema` |
| `Entities/Project` | 3 | `ConversionProject` (root), `ConversionJob`, `ConversionRun` (root) |
| `Entities/QaQc` | 2 | `ValidationIssue`, `ValidationReport` |
| `Events` | 2 | 8 events across project and run lifecycles |
| `Abstractions/Repositories` | 5 | `IRepository`, `IUnitOfWork`, three aggregate repositories |
| `Abstractions/Services` | 10 | `IDataSourceReader`, `IFeatureExporter`, `IAiClassifier`, `ICrsDetector`, `ICoordinateTransformer`, `IGeometryConverter`, `IQaQcEngine`, `IClock`, two catalogues |
| `Services` | 4 | `ExtentCalculator`, `AttributeSchemaFactory`, `ClassificationSubjectFactory`, `LinearUnitConverter` |
| `Specifications` | 4 | `ISpecification<T>`, composable `Specification<T>`, run specifications |
| `Validation` | 4 | `Guard`, `ValidationOutcome`, `ValidationFailure`, `IValidatable` |
| `Exceptions` | 6 | `DomainException` and five specific failures |

## Immutability

Not applied uniformly, because uniform application would be wrong.

| Category | Treatment | Why |
|----------|-----------|-----|
| Value objects | Fully immutable records / readonly record structs | Equality is by value; sharing must be free |
| Domain events | Immutable positional records | An event describes the past. A mutable one would be a statement about the past that changes |
| `GisFeature`, `GisDataset`, `ValidationIssue`, `ValidationReport` | Fully immutable | Produced once, then read concurrently by validation and several exporters at the same time |
| `ConversionProject`, `ConversionJob`, `ConversionRun` | Private setters, state changed only through named methods that enforce transitions | Entities have lifecycles; the invariant is *which transitions are legal*, not *no change* |
| `SourceDocument`, `SourceLayer`, `SourceElement` | Collections read-only; scalars settable | **Deliberate exception**, see below |

### The source model exception

A reader streams hundreds of thousands of elements and fills each in stages. Forcing immutable
construction would mean a builder object per element — allocation the hot path cannot justify.

So the scalars stay settable, but the *collections do not*: `Layers`, `Elements`, `Attributes`,
`Metadata` and `Warnings` are exposed as `IReadOnly…` and mutated only through `AddLayer`,
`AddElement`, `SetAttribute`, `SetMetadata` and `AddWarning`. That closes the leak that actually
mattered — an exporter reaching into a document it does not own and editing the attribute
dictionary — while leaving the streaming path allocation-free.

### `default(Extent)` is the empty extent

`Extent` stores `_hasValue` rather than `IsEmpty`. The inverse would make an uninitialised struct
claim to be a zero-sized box at the origin, and every accumulated extent would then be silently
dragged back to 0,0. Stored this way, `Extent.Empty.Union(x) == x` holds even for `default`.

## Dependency rule

```
Domain  ──▶  NetTopologySuite        (geometry value types only)
Domain  ──▶  nothing else
```

Every port an outer layer implements is declared here. PROJ, GDAL, ONNX Runtime, EF Core, the CAD
SDKs and the plugin host are all on the far side of an interface, which is what makes the domain
unit-testable with no native binaries installed.

## Ports declared

| Port | Implemented by |
|------|----------------|
| `IDataSourceReader` | CAD/IFC/DGN/PDF/point-cloud/LiDAR/drone plugins |
| `IFeatureExporter` | GIS Export plugin, built-in exporters |
| `IAiClassifier` | AI layer (`AiClassificationService`) |
| `ICrsDetector`, `ICoordinateTransformer`, `IGeometryConverter` | GIS layer (Module 3) |
| `IQaQcEngine` | QA/QC layer (Module 5) |
| `IRepository<,>`, `IUnitOfWork`, three aggregate repositories | Data layer (Module 6) |
| `IDataSourceReaderCatalog`, `IFeatureExporterCatalog` | `AiGisConverter.Composition` |

## Two kinds of validation, kept apart

- **`Domain/Validation`** — invariants. Is the *software* correct? `Guard`, `ValidationOutcome`,
  `DomainValidationException`. Violations are bugs.
- **`IQaQcEngine` (Module 5)** — data quality. Is the *data* fit to hand to a GIS? A
  self-intersecting ring is a perfectly valid object and an unacceptable parcel boundary.

## Aggregates

`ConversionProject` owns its jobs; `ConversionJob` has no repository, because a job saved
independently could be left in a state its project would have rejected.

`ConversionRun` is a separate root: runs are queried on their own axis ("what failed last night?")
and accumulate without bound, while a project is a small, long-lived object. Loading six months of
history to rename a project would be absurd.

`ConversionRun.Complete()` derives its terminal status rather than accepting one — a run with
error-level findings reports `SucceededWithWarnings` whatever the caller believes, so a batch
summary cannot claim a clean result over data that needs review.
