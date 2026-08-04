# AiGisConverter

AiGisConverter is a professional, high-performance CAD and BIM to GIS AI Platform. It provides a universal pipeline to extract, semantically enrich, classify, QA/QC, and export complex engineering models into standard GIS formats.

## Key Features

### 1. Interactive GIS Viewer & Mapping Editor
- **High-Performance Rendering:** Custom WPF `DrawingVisual`-based map renderer (`MapRendererControl`) designed to handle hundreds of thousands of geometries smoothly.
- **Interactive Map Tools:** Full support for Pan, Zoom, Fit Extents, Hover Highlight, and Multi-selection.
- **Universal Abstraction:** The renderer acts on normalized GIS geometry, entirely decoupled from native formats (DXF, Revit, IFC, Civil3D).

### 2. Professional Measurement & Snapping Engine
- **Extensible Snapping:** Comprehensive topological snapping including Vertex, Endpoint, Midpoint, Edge, Nearest, Intersection, and Center snaps.
- **Dynamic Measurement Tools:** Real-time calculation of Polylines (Distance/Length) and Polygons (Area/Perimeter).
- **Persistent Overlays:** Supports multiple concurrent measurements dynamically overlaid and tracked on the canvas.

### 3. Universal Semantic Feature Model
- **Canonical Geometries:** Preserves geometric fidelity using the foundational `SourceElement` model.
- **Semantic Enrichment (`SemanticFeature`):** Automatically maps BIM/CAD concepts (Category, Family, Level) from capable plugins directly to the generic data model.
- **Relational Topologies (`SemanticGraph`):** Maintains and translates complex BIM relationships (e.g., `BelongsTo`, `Contains`, `ConnectedTo`, `Intersects`) into the GIS domain.
- **Plugin Architecture:** Built-in `ISemanticProvider` capability adapters for Civil 3D, Revit, and IFC integrations.

### 4. AI Classification & QA/QC Engine
- **Classification Engine:** Evaluates geometries and semantic properties through configurable profiles, scoring candidate classifications using heuristics and semantic priorities.
- **Automated QA/QC:** Multi-stage validation covering Geometry, Attributes, CRS projections, and Topologies.
- **Relational Integrity:** Executes cross-feature semantic validations (e.g., `MissingHostRule` ensuring doors belong to walls).

### 5. Universal GIS Export
- Outputs standard formats (Shapefile, GeoPackage, etc.).
- Automatically maps AI classifications and semantically enriched fields (`SemanticCategory`, `SemanticFamily`, `SemanticLevel`) directly to the exported GIS schema.

## Getting Started

1. Open the solution in Visual Studio or your preferred IDE.
2. Build the solution using .NET 8.0 SDK.
3. Run the `AiGisConverter.Presentation` or `AiGisConverter.MappingEditor` project.
