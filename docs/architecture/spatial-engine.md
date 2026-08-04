# Spatial Processing Engine (Module 4)

`src/AiGisConverter.Gis/Spatial` — 11 source files, 69 test cases.
Pure NetTopologySuite: **no GDAL, no CAD, no AI, no plugin references** — verified by grep.

## What was already there

Module 3 shipped the CRS registry, EPSG lookup, coordinate and datum transformation, CRS
validation, self-intersection repair, ring repair, duplicate-vertex removal, simplification,
geometry validation, and the R-tree with Touches/Within/Contains/Overlaps/Intersects/Nearest/BBox.
The architecture is frozen, so this module **extends** rather than reimplements.

## What Module 4 adds

| Component | New | Notes |
|-----------|-----|-------|
| `ITopologyEngine` | ✅ | Standalone pairwise predicates + **Crosses** (absent before) + DE-9IM `Relate` |
| `ISpatialOperations` | ✅ | Buffer, Union, Intersection, Difference, SymmetricDifference, Dissolve, Merge, streaming Clip |
| `ISpatialAnalysis` | ✅ | Area, Length, Distance, Centroid, PointOnSurface, BBox, ConvexHull, Nearest |
| `GeodeticCalculator` | ✅ | Spherical measurement for geographic CRS |
| `ISpatialQueryEngine` | ✅ | Wraps the R-tree, adds **radius search** and predicate queries |
| `IGeometrySnapper` | ✅ | **Snap tolerance** — the one repair operation Module 3 lacked |

## The decision this module exists to make

NetTopologySuite computes **planar** area and length. Applied to EPSG:4326 that yields *square
degrees* and *degrees* — numbers that look plausible, propagate into a report, and are not areas
or lengths. A 1°×1° cell measures `1.0` planar; the true area is ~12,364 km².

So `ISpatialAnalysis` takes the coordinate system on **every** measuring method, asks the registry
whether it is geographic, and measures accordingly — geodesically in metres, or planar in the
system's own linear units. The `Measurement` result carries `IsGeodetic` and an accuracy note, so
no caller can report the wrong thing by forgetting to check.

### Verified before implementation

| Check | Result |
|-------|--------|
| Northern hemisphere ÷ full sphere | **0.5000** |
| London → Paris | 343.6 km (published ~344) |
| 1° longitude at equator | 111.195 km |
| 1° longitude at 60°N | 55.597 km (= equator × cos 60°) |
| 1°×1° cell at equator | 12,364 km² (ellipsoidal 12,308 — 0.45% high, documented) |

Spherical, not ellipsoidal: ~0.5% error on area, well inside CAD survey uncertainty and vastly
better than square degrees. Reproject and measure planar for survey-grade figures.

## Other decisions

**Overlays retry through repaired inputs.** Near-coincident edges defeat the noding constantly in
digitised survey data. Each overlay is attempted directly, then retried through `Buffer(0)`. One
bad parcel does not abort a district-wide dissolve.

**Bulk union is cascaded** (`UnaryUnionOp`), not pairwise-folded. Pairwise is O(n²) and turns a
dissolve from seconds into hours.

**Merge ≠ Union.** Merge collects parts losslessly; union dissolves shared boundaries and changes
the part count. Both are exposed because both are wanted.

**Clip streams and prepares the boundary.** Envelope rejection first, then a prepared-geometry
`Covers` fast path, then intersection only for genuine straddlers.

**Snapping is opt-in and refuses to lie.** It is destructive — a 50 mm kerb disappears under a
100 mm tolerance — so it is not in the default repair path, and a tolerance that collapses the
geometry returns a failure naming the problem rather than an empty result.

**Predicates return false on invalid geometry** rather than throwing. Conservative: it excludes a
pair from a result set rather than inventing a relationship.

## Remaining technical debt

1. **Nothing compiled.** Static checks only — 284 files, brace-balanced, namespaces match, usings resolve.
2. **Spherical, not ellipsoidal.** Karney's method would give millimetre accuracy; not implemented.
3. **`QueryRadius` sizes its candidate envelope with a flat 111,320 m/degree.** Correct at the equator, over-selects at high latitude — safe (extra exact evaluations) but not optimal.
4. **`Dissolve` buffers per group.** Cannot stream by definition; a single group larger than memory would fail.
5. **`UnitName` parses WKT by substring** rather than reading the unit authority code.
