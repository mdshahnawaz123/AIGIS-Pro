# QA/QC layer (Module 5)

`src/AiGisConverter.QaQc` — 17 source files, 42 test cases.
References **only** `AiGisConverter.Domain`. No CAD, no AI, no GIS, no plugin host — verified.

## Where it sits

Three things in this solution are called validation. They are not the same thing:

| | Question | Failure means |
|---|---|---|
| `Domain.Validation` | Is the **software** correct? | A bug |
| `Gis.GeometryValidator` | Is **this one geometry** sound? | Repair it, per feature, during conversion |
| **`QaQc` (this layer)** | Is the **dataset** fit to deliver? | Tell the operator before it ships |

The GIS layer already checks null, empty, zero-length, zero-area, self-intersection, degenerate
rings and duplicate vertices per feature. This layer does what that structurally cannot: look at
features *together*, at the schema, and at the coordinate system.

## Rules

| Rule | Severity | Catches |
|------|----------|---------|
| `Topology.Overlaps` | Error | Two parcels claiming the same ground. R-tree candidates, exact predicate, each pair reported once |
| `Topology.Dangles` | Warning | A pipe stopping 5 mm short of its junction — drawing looks connected, network isn't |
| `Topology.Slivers` | Warning | Digitising splinters, by thinness **and** absolute area |
| `Attribute.RequiredField` | Error | Missing mandatory values |
| `Attribute.DuplicateValue` | Error | Duplicated identifiers, which break every join a recipient attempts |
| `Export.FormatLimit` | Error / Warning | **Two field names truncating to the same 10 chars and silently merging** |
| `Crs.CoordinateOutOfRange` | Critical | Projected data labelled WGS 84; ungeoreferenced data sitting on the origin |
| `Dataset.Integrity` | Warning / Info | Empty datasets, unclassified layers, sparse fields |

## Calibration that matters

Sliver detection uses the isoperimetric quotient `4πA / P²` — 1 for a circle, 0.785 for a square,
tending to zero as a shape becomes a splinter. Verified numerically before implementation:

| Shape | Score | Verdict at 0.01 |
|-------|-------|-----------------|
| 100 m × 0.1 m digitising sliver | 0.0031 | flagged |
| 100 m × 1 m legitimate footpath | 0.031 | **not** flagged |

A threshold of 0.05 — the intuitive choice — would have flagged the footpath. Thinness is paired
with an absolute area cap so a long legitimate corridor survives too.

## Extensibility

`IValidationRule` is a **plugin-contributable capability**. A site ships its own submission checks
as a plugin; `CapabilityValidationRuleSource` in the composition layer surfaces them to the engine.
Neither `AiGisConverter.QaQc` nor the plugin host references the other.

## Containment

- **A rule that throws** becomes a finding of its own; the remaining rules still run.
- **Findings are capped per rule** (default 500) and the truncation is itself reported — silent
  truncation would make the report a lie.
- **Cross-feature rules are skipped above a feature ceiling** (default 250,000) and the skip is
  recorded.

## Reports

HTML, CSV and JSON. The HTML is self-contained — no external CSS, no fetched anything — because a
QA report is emailed, zipped and opened from a network share by people who will never have this
application installed. Every value is HTML-encoded: layer names come from CAD files written by
anyone.
