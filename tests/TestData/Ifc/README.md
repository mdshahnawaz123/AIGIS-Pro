# Real IFC test models

Drop production `.ifc` files here (IFC2x3 or IFC4). `RealModelTests` discovers every file in this
folder recursively and runs the full verification against each one. No code change is needed — add
a file, re-run the suite.

The folder is intentionally empty in the repository: real models are large and often confidential.
With no files present the tests pass trivially and `RealModelCoverage_IsReportedRatherThanAssumed`
records that zero real models were exercised — so a green suite never implies real-model coverage
that does not exist.

## What each model is checked against

Two kinds of check, kept separate on purpose.

**Invariants** (`RealModel_SatisfiesTheInvariantsDownstreamRelieson`,
`RealModel_ProducesAConsistentSemanticGraph`) must hold for any valid model, whatever the exporter
chose to write:

- element ids present and unique — export and selection sync key on them
- every `ParentId`, `HostId` and `ContainedInStoreyId` resolves to an element in the document
- every attribute value is a primitive the Attribute Table and export writers can render
- any geometry produced is valid
- every semantic feature carries a `Category`, or no classification rule can ever match it
- no dangling relationship edges in the semantic graph
- no non-finite quantity, which would poison the Statistics totals

**Coverage** (`RealModel_CoverageIsReported`) asserts nothing. It writes a per-model breakdown to the
test output — element count, declared length unit, and how many elements carried a storey, parent,
type object, material, classification or host. A model may legitimately contain no classifications,
so requiring them would fail on a valid file. Reporting them keeps "the IFC reader is verified"
honest: if every model on hand happens to have no type objects, that is visible rather than assumed.

## Useful models to add

Coverage is most valuable where hand-written fixtures cannot reach:

- an **IFC2x3** export alongside an IFC4 one — the reader is schema-neutral, and this is what proves it
- exports from **different authoring tools** (Revit, ArchiCAD, Tekla) — property-set naming and
  placement nesting differ per exporter
- a model with **deep spatial nesting** (site → building → storey → space)
- a **large** model, to exercise the same paths `IfcPerformanceTests` covers synthetically

## Performance and stress tests

`IfcPerformanceTests` generates its models rather than reading from this folder, so it needs no
input. It asserts on scaling ratios rather than wall-clock thresholds, which keeps it meaningful on
any machine: four times the elements costing roughly four times the time is linear, sixteen times is
quadratic.

The hundred-thousand-element stress test is gated, because writing and reading it costs minutes and
tens of megabytes of temporary disk. Run it deliberately:

```powershell
$env:AIGIS_STRESS = "1"
dotnet test tests\AiGisConverter.Plugins.Ifc.Tests
```

To skip the performance tests during ordinary development:

```powershell
dotnet test tests\AiGisConverter.Plugins.Ifc.Tests --filter "Category!=Performance"
```
