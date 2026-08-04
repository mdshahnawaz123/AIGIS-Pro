# Plugin system

```
Plugins
├── AutoCAD            host-bound  → IDataSourceReader   (bridge)
├── Civil3D            host-bound  → IDataSourceReader   (bridge)
├── Revit              host-bound  → IDataSourceReader   (bridge)
├── IFC                in-process  → IDataSourceReader
├── Bentley DGN        in-process  → IDataSourceReader
├── PDF                in-process  → IDataSourceReader
├── Point Cloud        in-process  → IDataSourceReader
├── LiDAR              in-process  → IDataSourceReader
├── Drone              in-process  → IDataSourceReader
├── GIS Export         in-process  → IFeatureExporter    (GeoJSON writer implemented)
├── AI Providers       in-process  → IAIProvider         (OpenAI-compatible, implemented)
└── Custom Extensions  in-process  → IDataSourceReader   (template, implemented)
```

## Projects

| Project | TFM | Role |
|---------|-----|------|
| `AiGisConverter.Plugins.Abstractions` | net8.0 | The SDK. The only assembly a plugin author references. |
| `AiGisConverter.Plugins.Hosting` | net8.0 | Discovery, ALC isolation, load/unload, capability registry. |
| `AiGisConverter.Bridge.Protocol` | **netstandard2.0** | Wire contract shared with add-ins running on .NET Framework. |
| `AiGisConverter.Bridge.Client` | net8.0 | Converter-side named-pipe client and `HostBoundReaderBase`. |
| `AiGisConverter.Composition` | net8.0 | Seam wiring plugin capabilities into the AI, CAD and GIS layers. |

## Load sequence

```
AddPluginSystem(configuration)      registers discovery, host, capability registry
AddAiLayer(configuration, ...)      registers built-in AI providers
AddPluginIntegration()              registers the capability adapters
        │
        ▼  (application start-up, after the container is built)
PluginBootstrapper.StartAsync()
        ├── PluginDiscovery      scan Plugins/, read plugin.json, validate — no assembly loaded
        ├── PluginHost           per plugin: new collectible ALC → load → ConfigureAsync → publish
        └── IAIProviderFactory.Refresh()      plugin AI providers become selectable
```

## Isolation rules

| Assembly | Resolved from | Why |
|----------|---------------|-----|
| `AiGisConverter.Plugins.Abstractions` | host | contract — type identity must be single |
| `AiGisConverter.Domain`, `.Ai`, `.Gis`, `.QaQc` | host | contract |
| `AiGisConverter.Bridge.Protocol` | host | contract |
| `NetTopologySuite` | host | geometry crosses the boundary |
| `Microsoft.Extensions.*.Abstractions` | host | `ILogger`, `IServiceCollection` cross the boundary |
| everything else | **plugin's own `.deps.json`** | lets two plugins carry conflicting versions |
| native libraries | **plugin's own folder** | LiDAR native binaries must not collide with GDAL's |

## Writing a plugin

```csharp
public sealed class MyPlugin : PluginBase
{
    public override string Id => "acme.reader.myformat";

    protected override Task OnConfigureAsync(IPluginRegistrationContext registration, CancellationToken ct)
    {
        registration.AddCapability<IDataSourceReader>(new MyReader(registration.Context));
        return Task.CompletedTask;
    }
}
```

```jsonc
{
  "id": "acme.reader.myformat",
  "name": "My Format Reader",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "entryAssembly": "Acme.Plugins.MyFormat.dll",
  "entryType": "Acme.Plugins.MyFormat.MyPlugin",
  "isolation": "Isolated",
  "capabilities": [ "DataSourceReader" ],
  "enabled": true,
  "loadOrder": 100
}
```

Drop the folder into `Plugins/` or `%LOCALAPPDATA%\AiGisConverter\Plugins\`. Nothing is
recompiled. Start from `plugins/AiGisConverter.Plugins.CustomExtensions`, which is a working
template.

## Configuration

```jsonc
"Plugins": {
  "SearchPaths": [ "Plugins", "%LOCALAPPDATA%\\AiGisConverter\\Plugins" ],
  "Enabled": [],                       // non-empty acts as an allowlist
  "Disabled": [ "aigis.reader.pdf" ],
  "FailFastOnLoadError": false,
  "LoadTimeoutSeconds": 60,

  "aigis.ai.providers": {              // each plugin's own section, by id
    "endpoints": [
      { "key": "lmstudio", "baseAddress": "http://localhost:1234/v1/", "model": "qwen2.5-7b-instruct" }
    ]
  }
}
```

## Host-bound plugins

```
AI GIS Converter (net8.0)                 AutoCAD / Civil 3D / Revit
┌──────────────────────────┐              ┌──────────────────────────────┐
│ RevitPlugin              │              │ AI GIS Converter add-in      │
│   RevitReader            │  named pipe  │   pipe server                │
│   : HostBoundReaderBase  │◄────────────►│   Revit API on the UI thread │
│   NamedPipeBridgeClient  │  JSON + WKT  │   Bridge.Protocol (ns2.0)    │
└──────────────────────────┘              └──────────────────────────────┘
```

`HostBoundReaderBase` owns transport, timeouts, error mapping and geometry mapping. `AutoCadReader`,
`Civil3DReader` and `RevitReader` differ only in format key and extensions — roughly fifteen lines
each.

A missing host application is an ordinary condition, not an exception: the read returns a failed
`Result` reading *"Revit is not running, or the AI GIS Converter add-in is not loaded."*

## Status

| Component | State |
|-----------|-------|
| SDK, host, ALC isolation, discovery, capability registry | implemented |
| Bridge protocol, client, `HostBoundReaderBase` | implemented |
| GeoJSON exporter | implemented |
| OpenAI-compatible AI provider plugin | implemented |
| Delimited-point reader (template) | implemented |
| IFC, DGN, PDF, point cloud, LiDAR, drone readers | contract + detection complete; **format backend not bound** |
| AutoCAD / Civil 3D / Revit add-ins (host side) | **not written** — the converter side is complete |
