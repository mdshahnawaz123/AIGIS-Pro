# CAD layer

`src/AiGisConverter.Cad` — 21 source files, 9 test files, 80 test cases.
Compiles and runs with **no Autodesk SDK**. Verified: zero `Autodesk.*` type references in the
solution; every occurrence of the word is prose in a doc comment.

## Shape

```
ICadProvider                         one engine per CAD format
├── DxfProvider      .dxf            netDxf. Always available.
└── AutoCadProvider  .dwg            Autodesk-free. Delegates to IDwgBackend.
        └── IDwgBackend
            ├── UnavailableDwgBackend   default — reports its own absence
            └── (Interop/)              licensed engine, excluded from compilation
                                        unless -p:EnableAutoCadProvider=true

CadProviderFactory  → resolves by ICadProvider.CanRead, never by a switch on extension
CadDataSourceReader → adapts each provider to the domain's IDataSourceReader
```

## The netDxf boundary

Exactly **two** files reference netDxf: `Providers/Dxf/DxfProvider.cs` and
`Providers/Dxf/NetDxfEntityConverter.cs`. Both carry a header saying so.

Neither performs a geometric calculation. They pull numbers out of netDxf objects and hand them to
`AiGisConverter.Cad.Geometry`, which is vendor-free and unit-tested. A netDxf major-version rename
therefore touches two files and no maths.

## Entities read

| Requested | Produced | Notes |
|-----------|----------|-------|
| Layers | `SourceLayer` + colour, linetype, frozen state | Hidden and frozen layers filtered by configuration |
| Blocks | Point *or* exploded geometry | `ExplodeBlocks` decides; nesting bounded at 8 |
| Attributes | Feature attributes on the block's elements | Where drawings actually keep asset data |
| Lines | `LineString` | |
| Polylines | `LineString`, or **`Polygon` when closed** | Bulges expanded to arcs |
| Circles | `Polygon` | |
| Arcs | `LineString` | |
| Hatches | `Polygon` / `MultiPolygon` with holes | Containment decides holes, not island flags |
| Text, MText | `Point` + `Text` | Carried as `GeometryKind.Annotation` |
| Ellipses, Splines, Points | as appropriate | Beyond the brief; free from the same machinery |

Unsupported entity types are reported **once per type**, not per entity — a file with fifty
thousand proxy entities produces one warning, not fifty thousand.

## Three decisions worth review

**Tessellation is tolerance-driven, not a fixed segment count.** A fixed count is wrong at both
ends of the scale: thirty-two segments is wasteful on a 50 mm fillet and visibly polygonal on a
500 m highway curve. `ChordTolerance` expresses what a surveyor actually cares about — how far the
approximation may stray. Segment count follows from `a = 2·acos(1 − tol/r)`.

**Closed polylines become polygons.** To CAD a closed LWPOLYLINE is a line that returns to its
start; to a GIS it is an area. Emitting a closed `LineString` is legal and almost never what the
user wanted after export.

**Hole detection is geometric.** DXF island flags are widely wrong in files from third-party
exporters, so `PolygonAssembler` counts containment depth instead: a ring nested inside an odd
number of others is a hole, an even number makes it solid again.

## Maths verification

The geometry is pure and was checked numerically before the tests were written:

| Property | Result |
|----------|--------|
| Bulge arc terminates exactly on the second vertex | max error 1e-9 over 20,000 random cases |
| Every tessellated point lies on the derived circle | max radial error < 1e-6 |
| Chord deviation stays within the configured tolerance | satisfied in all cases |
| Polygon nesting areas (donut, disjoint, island-in-hole) | 84 / 125 / 292, all exact |
| Zero-area collinear ring rejected | NTS validates closure but *not* area |

## Configuration

```jsonc
"Cad": {
  "Tessellation": { "ChordTolerance": 0.01, "MinimumSegments": 4, "MaximumSegments": 512 },
  "IncludeInvisibleLayers": false,
  "ExplodeBlocks": false,
  "ReadBlockAttributes": true,
  "AssumedUnits": "Unknown",
  "ReadCrsSidecar": true,
  "MaxElements": 0
}
```

`ChordTolerance` is in **drawing units**, which is why unit detection runs first. A drawing in
millimetres and one in metres need different numbers.

Unsupported `$INSUNITS` codes map to `LinearUnit.Unknown` rather than a nearest guess: an
order-of-magnitude scale error is undetectable downstream, whereas `Unknown` makes the conversion
ask.

## What is deliberately absent

No GIS export, no reprojection, no AI classification. Providers return domain models only.
DWG requires a licensed engine; the stub explains that in one sentence rather than throwing a
missing-assembly exception naming `acdbmgd`.
