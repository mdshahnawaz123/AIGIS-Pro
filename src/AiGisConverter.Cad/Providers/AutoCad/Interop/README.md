# AutoCAD interop

Files in this folder are **excluded from compilation** unless the build sets
`EnableAutoCadProvider=true` and points `AutoCadSdkPath` at an ObjectARX / RealDWG SDK:

```powershell
dotnet build -p:EnableAutoCadProvider=true -p:AutoCadSdkPath="C:\ObjectARX 2025\inc"
```

This is the only place in the solution where `Autodesk.*` types may appear. Everything else —
including `AutoCadProvider` itself — is Autodesk-free and compiles on a bare machine.

To add a licensed DWG engine, implement `IDwgBackend` here and register it in place of
`UnavailableDwgBackend`:

```csharp
services.AddSingleton<IDwgBackend, RealDwgBackend>();
```

Nothing else in the CAD layer changes.
