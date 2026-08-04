# ADR 0002 — Plugin architecture

- **Status:** Accepted
- **Date:** 2026-07-29
- **Projects:** `AiGisConverter.Plugins.Abstractions`, `.Plugins.Hosting`, `.Bridge.Protocol`, `.Bridge.Client`, `.Composition`

## Context

Twelve categories of extension were required from the outset: AutoCAD, Civil 3D, Revit, IFC,
Bentley DGN, PDF, point cloud, LiDAR, drone, GIS export, AI providers and custom extensions.
They are not homogeneous, and pretending otherwise would have produced the wrong abstraction.

## Decisions

### 1. The SDK provides a mechanism, not a taxonomy

A first design gave `IPluginRegistrationContext` an enumeration of capability kinds
(`AddReader`, `AddExporter`, `AddAiProvider`). Rejected: every new kind of extension would then
require editing the SDK — the exact modification the plugin system exists to prevent.

Registration is instead open and type-keyed:

```csharp
registration.AddCapability<IDataSourceReader>(new IfcReader(context));
```

Contracts live in the layer that owns them (`IDataSourceReader` and `IFeatureExporter` in Domain,
`IAIProvider` in the AI layer). The SDK references none of them by name. Adding a CRS resolver or
a QA/QC rule pack as a plugin capability requires no SDK change at all.

### 2. Collectible `AssemblyLoadContext` per plugin

An IFC plugin and a point-cloud plugin will carry incompatible versions of the same JSON, maths or
native library. `AssemblyDependencyResolver` over each plugin's own `.deps.json` lets both load.

The counterpart rule is that contract assemblies must **not** be duplicated. A second copy of the
SDK inside a plugin context produces a second `IDataSourceReader` type with the same name and
different identity, and every cast at the boundary fails with a message that appears to claim a
type cannot be converted to itself. Names in `PluginOptions.SharedAssemblies` therefore return
`null` from `Load`, deferring to the default context; and plugin projects set `Private=false` on
contract references so the copy never exists in the first place.

### 3. Manifest-first: decide before loading

`plugin.json` carries the id, SDK version, entry assembly, capabilities and host requirement.
Compatibility, enablement and duplicate-id detection are answered from the manifest alone. A
plugin built against an incompatible SDK is rejected without ever being mapped into the process,
which turns an unrecoverable `TypeLoadException` during start-up into a line in the plugin manager.

### 4. Host-bound plugins split into add-in plus bridge

The AutoCAD, Civil 3D and Revit APIs only function inside their own application's process. Each is
therefore two pieces: an add-in installed into the host application, and a converter-side plugin
that is a bridge client. They exchange newline-delimited JSON over a named pipe, with geometry as
WKT.

`AiGisConverter.Bridge.Protocol` targets **netstandard2.0** deliberately — a Revit 2024 add-in runs
on .NET Framework 4.8 and cannot reference a net8.0 assembly. It is the only assembly that crosses
that boundary; the SDK itself stays net8.0.

One connection per request rather than a persistent session: a CAD application can be closed at any
moment, and a long-lived pipe would leave the converter blocked on a handle that will never
complete.

### 5. Failure is contained, not fatal

A plugin that throws while loading is marked failed, its context released, and loading continues.
On a workstation without Revit installed, the Revit plugin fails and the application still opens.
`FailFastOnLoadError` exists for CI, and defaults to false.

Likewise `IPlugin.ConfigureAsync` may register nothing. A plugin whose prerequisites are absent
should log why and contribute no capability, rather than throw.

### 6. `AiGisConverter.Composition` is the seam

`AiGisConverter.Ai` must not reference the plugin host; the plugin host must not reference the AI,
GIS or QA/QC layers. Everything that would force such a reference lives in `Composition`:
`CapabilityAIProviderSource`, `PluginAwareDataSourceReaderCatalog`,
`PluginAwareFeatureExporterCatalog` and `PluginBootstrapper`.

This forced one change to Module 4. `AIProviderFactory` previously indexed providers in its
constructor; plugin-contributed providers do not exist at that moment. The factory now builds its
index lazily from `IEnumerable<IAIProviderSource>` and exposes `Refresh()`, which
`PluginBootstrapper` calls after loading. That ordering dependency is easy to get silently wrong,
so it lives in one named class rather than in start-up code.

## Consequences

**Positive**

- A new format or AI provider ships as a folder. No core file changes; verified by build checks.
- Vendor SDK version conflicts are structurally impossible between plugins.
- The application runs with zero plugins installed.
- First-party plugins deploy in exactly the shape a third party would use.

**Negative**

- Unloading is best-effort. A collectible context is released only when nothing references anything
  inside it, and the host cannot force that. `PluginHost.ReleaseContext` reports failure to collect
  rather than hiding it, naming the plugin responsible.
- Contract assemblies are now versioned public API. `AiGisConverter.Plugins.Abstractions` and
  `.Bridge.Protocol` are pinned to `AssemblyVersion 1.0.0.0`; breaking either breaks every plugin
  in the field.
- The bridge serialises geometry as WKT, which costs parse time on large drawings. Accepted because
  it is the one representation both sides of a .NET Framework / .NET 8 boundary can agree on
  without a shared binary layout.
