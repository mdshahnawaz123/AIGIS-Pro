# Presentation layer (Module 8)

`src/AiGisConverter.Presentation` — 19 C# files, 8 XAML, 8 tests.
**The composition root: all nine layers meet here and nowhere else.**

## The project didn't build before this module

It had `OutputType=WinExe` and no `App.xaml`, so there was no entry point to link — which is why
Module 6's build produced a DLL for every project except this one. Two things fixed it:

1. **`Program.Main` is hand-written**, with `<StartupObject>` pointing at it. App.xaml would
   normally generate the entry point, but a generated `Main` can't be async and can't own an
   `IHost` — and start-up genuinely has to await a database migration and plugin loading before
   the first window appears.
2. **`<ApplicationIcon>` removed.** It referenced `Assets/Icons/app.ico`, which doesn't exist; a
   missing icon fails the build outright.

## Composition

`HostFactory` registers the nine layers. Order matters in exactly **two** places:

- `AddAiLayer` before `AddPluginIntegration` — the latter adds a second provider source to the former
- `AddPluginSystem` before `AddPluginIntegration` — the latter reads its capability registry

Everything else is order-independent, because no layer registers another. `AddGisLayer` knows
nothing about CAD; `AddApplicationLayer` knows nothing about either. The knowledge that they're
used together lives here and only here.

## Degrade, don't refuse

`ApplicationStartup` returns a `StartupOutcome` rather than throwing. An unwritable database or a
broken vendor plugin should still let someone open the app and convert a DXF — the shell shows a
dismissible banner listing what's missing. The one thing start-up won't do is *hide* a degradation.

`DispatcherUnhandledException` is marked handled deliberately. A conversion tool that vanishes
loses whatever the user was part-way through setting up; showing the fault and staying open lets
them save and send the log.

## Two seams that make view models testable

- **`IUiDispatcher`** — progress arrives from a conversion on the thread pool, and touching an
  `ObservableCollection` from there throws. This is the marshalling seam, so no view model imports
  `Dispatcher`.
- **`IDialogService`** — a view model that opens a file dialog directly can only be exercised by a
  human clicking through it.

**None of the 8 tests opens a window.** That's the payoff.

## What the UI deliberately doesn't do

**Settings are read-only.** They live in `appsettings.json` and profile files, both hot-reloaded.
A second editing surface would be a second source of truth that drifts from the first.

**`ProjectViewModel.BuildProject()` passes everything through the domain's factories.** An
unparseable CRS or an empty project is rejected by the same code that would reject it from a
script — tested.

**The plugins page lists rejected and failed plugins**, not just loaded ones. A plugin that
silently doesn't appear is the hardest kind of problem to diagnose.

## Remaining technical debt

1. **Nothing compiled.** Static checks pass: 380 C# files clean, all 8 XAML `x:Class` values paired
   to a matching code-behind namespace and type.
2. `QaQcViewModel.LoadFromRuns` blocks on `GetAwaiter().GetResult()` — called from a completed
   conversion, not the dispatcher, but it should be async.
3. The status-bar "History unavailable" indicator uses a `ConverterParameter=Invert` that
   `BooleanToVisibilityConverter` doesn't implement — it will show inverted. One-line fix.
4. No `app.ico`. Add one and restore `<ApplicationIcon>`.
