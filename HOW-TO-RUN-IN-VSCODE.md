# AiGisConverter — full solution (Phase 1 applied)

This is your complete GitHub repo (`440 .cs`, 43 projects) with the **Phase 1** changes already applied:
- **H1** — `FeatureBuilder` never emits null geometry (skip when configured, else valid empty geometry).
- **C1** — `IfcReader` bound to **xBIM Essentials 6.0.445** (reads `.ifc` → SourceElements + semantic attributes).
- New H1 regression tests in `AiGisConverter.Gis.Tests`.

## Prerequisites
- .NET 8 SDK, **x64** (native GDAL/PROJ are x64-only).
- Windows for the two WPF projects (`AiGisConverter.Presentation`, `AiGisConverter.MappingEditor`); the rest is cross-platform.
- Internet access for NuGet the first time (restores NetTopologySuite, xBIM, EF Core, GDAL, xUnit, …).

## Build & test
```
dotnet restore
dotnet build -c Release
dotnet test
```
If `Xbim.Essentials 6.0.445` doesn't resolve, bump it to the latest 6.0.x in `Directory.Packages.props`.

## Try the IFC reader
A minimal valid IFC fixture is in `samples/sample.ifc` (2 walls on Level 1, a FireRating Pset).
Run the IFC plugin against it and export GeoJSON; confirm 2 `IfcWall` elements with placement points and no null geometries.

## Notes
- `bin/`, `obj/`, `.git/`, and build artifacts were excluded from this zip — VS Code / `dotnet restore` regenerate them.
- Source of truth remains your GitHub repo; treat this as a working copy.
