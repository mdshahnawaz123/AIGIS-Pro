# Application layer (Module 7)

`src/AiGisConverter.Application` — 17 source files, 19 tests.
References **only** `AiGisConverter.Domain`. Verified: it names no module type directly.

## How it coordinates five modules without referencing any of them

Every capability arrives as a Domain port, which is what Module 1's port design was for:

| Stage | Port | Owned by |
|---|---|---|
| Read source | `IDataSourceReaderCatalog` | CAD + reader plugins |
| Detect CRS | `ICrsDetector` | GIS |
| Classify | `IAiClassifier` | AI |
| Convert geometry | `IGeometryConverter` | GIS |
| Validate | `IQaQcEngine` | QA/QC |
| Export | `IDatasetExportService` * | GIS, via Composition |

\* Two capabilities had no Domain port: writing `GisDataset`s (the domain's `IFeatureExporter`
takes a `SourceDocument` — different shape) and rendering QA reports. Application declares
`IDatasetExportService` and `IQaReportRenderer`; **Composition** adapts them. Neither layer learns
about the other.

## No business rules — how that's enforced

A stage calls the module that owns the decision, puts the answer on the context, and reports
whether it could. Nothing here holds a tolerance, threshold or rule.

The one place it would have crept in: **the classification vocabulary had no owner anywhere.**
Rather than inventing a list in the orchestration layer, I added `CandidateFeatureClasses` to
`ConversionSettings` (Domain) — a project decision, captured with the settings a run records.
**This is the only Domain change in Module 7**, additive and non-breaking.

Similarly, `ConversionRun.Complete()` *derives* the terminal status; `ConversionService` records
whatever the aggregate decided rather than choosing.

## Error recovery, stated once

`IPipelineStage.IsOptional` is the whole policy. Classification is optional — an unreachable model
leaves layers unclassified rather than abandoning a usable conversion. Reading the source is not.
Declaring it on the stage means adding a stage doesn't mean editing a list of exceptions.

A stage that **throws** is contained and converted to a failure. Stages are meant to return
`Result`, but a plugin-contributed reader or vendor SDK is not this codebase and will throw
eventually — containment stops it unwinding through the batch and taking the other files with it.

## Batch

Concurrency defaults to **2**, not core count. Conversion is memory-bound — a large drawing holds
its whole source model — so one per core is the fastest way to exhaust a workstation, not to
finish. Each job gets its own DI scope, because the EF change tracker is scoped and parallel
conversions sharing one would write to the same tracker.

## Remaining technical debt

1. **`GisDatasetExportService` does not stream.** The domain's `IGeometryConverter` returns a
   materialised list; widening it would change a frozen contract. Large drawings should use the GIS
   engine's own `ConvertAndExportAsync` rather than this pipeline. Documented at the seam.
2. `JobEngine` duplicates `BackgroundTaskQueue` in Infrastructure — Application can't reference
   Infrastructure (the dependency runs the other way), so it has its own Channel.
3. Nothing has been compiled. Static checks only: 361 files clean.
