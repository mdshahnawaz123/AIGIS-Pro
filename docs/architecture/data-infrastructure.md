# Data & Infrastructure (Module 6)

`AiGisConverter.Data` — 14 files, references **only** Domain.
`AiGisConverter.Infrastructure` — 7 files, references Domain + Application.
30 test cases. All 341 solution files pass structural checks.

## What is stored, and what is not

Nothing spatial. Converted features go to GIS files; the database keeps the **record** — what was
converted, under which settings, and what QA found. The things somebody asks about six months
later.

Two aggregates (`ConversionProject`, `ConversionRun`) and the findings belonging to runs.

## Mapping decisions

**Typed identifiers become `Guid` columns.** The wrapper exists to stop a run id being passed where
a job id belongs; that is a compile-time protection with no reason to reach storage.

**Composite value objects become JSON text** — settings, source references, coordinate systems.
They are read as a unit and never filtered on, so shredding them into columns would buy
queryability nobody wants at the cost of a migration every time a setting is added.

**What *is* queried stays a real column**: run status, timestamps, severity, project id. That's the
axis run history is actually searched on — "what failed last night", "what's older than the
retention window".

**Collections map through their backing fields.** `ConversionProject.Jobs` and
`ConversionRun.OutputPaths` are `IReadOnlyList` with no setter, because the aggregates mutate only
through `AddJob` and `RecordOutput`. EF respects that rather than reaching for a setter the domain
deliberately doesn't have.

## Domain events

`UnitOfWork` collects events **before** the save, clears them from the aggregates, saves, then
dispatches. Both orderings matter:

- Dispatch after commit — an event announcing a finished run that is then rolled back leaves every
  handler acting on something that did not happen.
- Clear before dispatch — a handler causing another save must not find the same events queued again.

`IDomainEventDispatcher` is declared in **Data**, not Infrastructure. The frozen dependency rule
puts Infrastructure downstream of Data, so a dispatcher defined there would be unreachable from the
unit of work that needs it. Default implementation discards; the composition root supplies a real
one.

## Pruning

Run history is the only thing here that grows without bound. `PruneAsync` and
`DatabaseInitialiser.PruneHistoryAsync` use `ExecuteDeleteAsync` — set-based, not by loading
aggregates. Nightly batches accumulate hundreds of thousands of runs, and materialising them to
call `Remove` on each turns routine maintenance into an out-of-memory failure. Findings are deleted
first; SQLite won't order it for us when the delete is expressed against two sets.

## Infrastructure

| Service | Note |
|---|---|
| `SystemClock` | So retention windows are testable by stating "now" rather than waiting for it |
| `PhysicalFileSystem` | Narrow by design. `CanWriteTo` **writes a probe** rather than inspecting permissions — on Windows the effective right depends on token, share, ACL and redirection |
| `EnvironmentSecretResolver` | Process → User → Machine. Keys never appear in `appsettings.json`, so never in the repo, the installer, or a support bundle |
| `BackgroundTaskQueue` | **Bounded.** An unbounded queue turns a held-down button into an OOM; back-pressure is the only honest signal to the producer |
| `SerilogConfigurator` | Falls back to a working default if the config section is missing or malformed — a logging misconfiguration must not be the thing that stops start-up, because then nothing records why |

## Tests

SQLite **in-memory**, not the EF in-memory provider. The in-memory provider isn't relational — no
transactions, no foreign keys, no SQL translation — so a test passing against it says nothing about
whether the mapping works. Reads go through a *second* context over the same connection; reading
back through the writer would only prove the change tracker remembers.

## Remaining technical debt

1. **No migration exists.** `DatabaseInitialiser` calls `MigrateAsync`, which needs
   `dotnet ef migrations add Initial` run once. `EnsureCreated` was rejected deliberately: it
   leaves no migration history, and the first schema change can't upgrade an existing database.
   The tests use `EnsureCreated` because they build a throwaway schema.
2. **EF mapping is the highest-risk part of this module** and is unverified — private constructors,
   backing-field navigations and value converters are exactly what a first build finds fault with.
3. `ValidationReportRepository.GetForRunAsync` returns null for a run with zero findings,
   indistinguishable from a run that was never validated.
