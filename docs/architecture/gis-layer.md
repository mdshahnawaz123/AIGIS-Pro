# GIS layer

`src/AiGisConverter.Gis` — 36 source files, 15 test files, 85 test cases.
**Zero CAD coupling, verified**: no `netDxf`, no `Autodesk`, no reference to `AiGisConverter.Cad`.
The only project reference is `AiGisConverter.Domain`.

## Pipeline

```
SourceDocument ──▶ GisConversionEngine
                        │
                        ├─ ProfileRepository        which profile governs this run
                        ├─ AttributeMapper          derive schema per layer
                        └─ FeatureBuilder ─────────────────────────────┐
                             validate → repair → map → simplify        │  one feature at a time,
                             → transform → snap precision              │  never accumulated
                                                                       ▼
                                                          IStreamingExporter
                                                          ├─ managed: GeoJSON, CSV, KML, WKT, WKB
                                                          └─ OGR:     Shapefile, GeoPackage
```

Stage order is deliberate: mapping and repair run in **source** coordinates, because tolerances are
expressed in source units and a repair judged against a metre threshold means nothing once the data
is in degrees. Reprojection comes next, precision snapping last, so the snapped grid is the one the
output is actually written on.

## Two entry points

| Method | Returns | Use |
|--------|---------|-----|
| `ConvertAsync` | `Result<IReadOnlyList<GisDataset>>` | Satisfies the **frozen** domain port `IGeometryConverter`. Materialises. Interactive path only. |
| `ConvertAndExportAsync` | `Result<GisConversionOutcome>` | Streams source → writer. **Use this for anything large.** |

The domain port's signature cannot express streaming, and the architecture is frozen, so the
streaming capability is offered *alongside* the port rather than by widening it.

## GDAL containment

Four files reference GDAL — `Gdal/GdalEnvironment.cs`, `Crs/GdalCrsRegistry.cs`,
`Crs/GdalCoordinateTransformer.cs`, `Exporters/Ogr/OgrExporterBase.cs`. Each carries a header
saying so. Native-load failure is **recorded, not thrown**: GeoJSON, KML, CSV, WKT and WKB keep
working on a machine with a broken native deployment.

## Formats

| Format | Engine | Notes |
|--------|--------|-------|
| GeoJSON | managed | RFC 7946. Emits legacy `crs` for projected data — shipping eastings labelled as longitudes is worse than a non-conformant hint |
| CSV | managed | RFC 4180, UTF-8 BOM so Excel doesn't mangle non-ASCII |
| KML | managed | OGC 2.2. **Critical finding** if data isn't WGS 84 — KML opens without error in the wrong place |
| WKT / WKB | managed | Geometry only + `.prj` sidecar. WKB framed with LE int32 length prefixes (local convention, documented) |
| Shapefile | OGR | Reports all five files. Writes `.cpg` explicitly |
| GeoPackage | OGR | Transaction-batched — untransacted SQLite insert-per-row is ~100× slower |

## Profiles

`generic-geojson` ← `esri` ← `dubai-municipality`, plus `qgis`. Embedded as resources; a user file
of the same `id` in a search path replaces the built-in entirely.

⚠️ `dubai-municipality` sets `EPSG:3997` from general knowledge. **Verify against the current DM
submission specification before production use.**
