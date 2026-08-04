# AI GIS Converter — Module 9 Final Report
**Version 1.0 production-readiness assessment**
Date: 30 July 2026 · Scope: Modules 0–9 · Solution: 38 projects (15 src, 12 plugins, 11 tests)

---

## 0. The one thing to read first

**Six of the fifteen source projects have never been compiled, and the WPF application has never been built at all.**

I verified this by timestamp, not by assumption. Every `src/*/bin/**` DLL was compared against the modification time of every `.cs` file in that project:

| Project | Build state | Evidence |
|---|---|---|
| Presentation | **never built** | no DLL exists; 19 source files |
| Application | **never built** | 17 of 17 files newer than the DLL |
| QaQc | **never built** | 17 of 17 files newer than the DLL |
| Data | **never built** | 14 of 14 files newer than the DLL |
| Infrastructure | **never built** | 7 of 7 files newer than the DLL |
| Business | **never built** | DLL exists, 0 source files (Module 0 placeholder) |
| Gis | partially stale | 13 of 47 files newer (Spatial engine post-dates the build) |
| Ai | partially stale | 3 of 54 newer (the cache-clone fix post-dates it) |
| Composition | partially stale | 3 of 7 newer |
| Domain | partially stale | 1 of 75 newer (`CandidateFeatureClasses`) |
| Cad, Bridge.Client, Bridge.Protocol, Plugins.Abstractions, Plugins.Hosting | current | 0 files newer |

The last successful build was **29 July 2026, 17:22** — it covers Modules 0–2 and the plugin infrastructure, and predates the Spatial engine, QA/QC, Data, Infrastructure, Application and Presentation layers entirely.

Where a DLL exists but every source file is newer, that DLL is the empty Module 0 scaffold, not a compilation of the code as it stands.

**Consequence for this report:** every number below that is not a build result is a *static* result. The 449 test cases have never been executed. The correctness claims rest on source analysis, on numeric models I ported to Python and cross-checked, and on the structural verifier — not on a compiler. **The readiness score and the release decision are conditioned on a first successful build and a first green test run.** No assessment can promote itself past that gate, and this one does not.

---

## 1. Overall architecture review

**Verdict: the architecture is sound and, unusually, it is structurally enforced rather than merely documented.**

### Layering — verified clean

I computed the full transitive project-reference closure for all 13 code-bearing source projects:

- **0 circular references.**
- **0 layering violations.** Each project reaches exactly the closure it was designed to reach and no more.
- Domain reaches **nothing** — no other AiGisConverter assembly, no GDAL, no netDxf, no Autodesk, no EF Core. Its single package reference is NetTopologySuite. This is the load-bearing property of the whole design and it holds exactly.
- Application reaches **only Domain**. It orchestrates CAD, GIS, AI and QA/QC without naming any of them, through ports declared in Domain.
- No layer holds an upward reference to Composition or Presentation.

### Boundary containment — verified by file count

The design's central bet is that third-party dependencies are confined to named adapter files, so they can be replaced without touching business code. That bet pays off, measurably:

| Dependency | Files permitted to see it | Actual |
|---|---|---|
| netDxf | 2 (`DxfProvider`, `NetDxfEntityConverter`) | **2** |
| GDAL/OGR | 4 (`GdalEnvironment`, `GdalCrsRegistry`, `GdalCoordinateTransformer`, `OgrExporterBase`) | **4** |
| Autodesk | 0 outside `Providers/AutoCad/Interop/**` (compile-gated) | **0** `using Autodesk` in all of `src/` |

The solution builds and runs with **no Autodesk SDK installed** — the standing constraint from Module 2 onward. `AutoCadProvider` delegates to `IDwgBackend`; the only Autodesk-touching code sits behind a compile gate and an out-of-process netstandard2.0 bridge.

### Open/Closed — enforced structurally

There is **no `switch` on a provider key, format key or profile key anywhere outside the folder that owns that concept.** Adding a fifth AI provider, an eighth exporter or a fifth conversion profile requires zero edits to existing files. This was checked by pattern-scanning all 307 source files, not asserted.

### Plugin isolation — verified

All **12 of 12** plugin projects mark their contract references `Private=false`. The staged output in `artifacts/plugins/` confirms the containment worked in the real build: each plugin folder contains 3 files, not a copy of the contract assembly. Loading uses a collectible `AssemblyLoadContext` with `AssemblyDependencyResolver` and shared-contract deflection, so a plugin cannot fork the contract types.

### Where the architecture is weakest

1. **`AiGisConverter.Business` is an empty project.** It has a DLL and zero source files. Business rules live in Domain (correctly) and in the module layers (correctly). The project is a Module 0 artefact that never acquired a purpose. It should be **deleted**, not filled — filling it would mean moving logic out of the modules that own it.
2. **Composition carries real logic.** `ApplicationAdapters` builds output paths, chooses exporters and applies naming — that is closer to policy than to wiring. It is small and correct, but it is the file most likely to accumulate the logic nobody could place elsewhere. Watch it.
3. **The Presentation layer is unproven in a way the others are not.** WPF failures are overwhelmingly startup-time and XAML-time — exactly the class of failure that static analysis cannot see and that only running the app reveals.

---

## 2. Production readiness score

### 65 / 100 — as it stands today

The score is dominated by one factor. Breaking it out:

| Dimension | Weight | Score | Notes |
|---|---:|---:|---|
| Architecture & design | 20 | **19** | Clean, enforced, genuinely extensible |
| Domain correctness | 15 | **13** | Numerically cross-checked; immutable; Result-based |
| Test *design* | 15 | **11** | 425 cases, well-targeted; UI and end-to-end thin |
| **Test *execution*** | **15** | **0** | **Never run. Not once.** |
| **Build integrity** | **15** | **3** | 6 projects never compiled; the app never built |
| Security | 10 | **7** | Encoding solid; overwrite policy unenforced |
| Observability & ops | 5 | **4** | Serilog throughout, structured, correlated |
| Documentation | 5 | **5** | Per-layer docs, 2 ADRs, XML docs solution-wide |
| **Total** | **100** | **65** | |

**The same solution scores an estimated 85–88 the moment it compiles clean and the tests pass green.** Thirty points are sitting behind a gate that one successful `dotnet build` and one `dotnet test` will open — or will re-price sharply downward. I cannot tell you which from here, and I will not pretend otherwise: with `TreatWarningsAsErrors=true` across 27,539 lines that have never met a compiler, a first build producing zero errors would be a surprising outcome. Expect a first-build error count in the tens, dominated by missing usings, nullable-annotation mismatches, and XML-doc warnings promoted to errors.

### Per-dimension ratings (out of 10)

Each score is given twice: **now** (with nothing compiled) and **post-build** (my expectation once Gates 1–3 in §8 are passed). The gap between the two columns is the honest measure of how much of this assessment is currently unbacked.

| Dimension | Now | Post-build | Reasoning |
|---|---:|---:|---|
| Architecture | **9** | 9 | Layering enforced, not just documented. 0 violations, 0 cycles, boundary files at exactly their designed counts |
| Maintainability | **9** | 9 | XML docs solution-wide, per-layer architecture docs, 2 ADRs, no switch-on-key outside owning folders |
| Performance | **5** | 7 | Streaming design is right; nothing measured. `Buffer(0)` on invalid geometry is the unquantified risk |
| Security | **7** | 8 | Encoding and escaping solid across all four output formats; unconditional overwrite is the open wound |
| Scalability | **6** | 8 | `IAsyncEnumerable` + bounded channels are the correct primitives; concurrency limits never exercised |
| Reliability | **4** | 8 | `Result`-based error handling is consistent and thorough — but no failure path has ever actually run |
| Thread safety | **7** | 8 | AI cache audited and fixed; two sync-over-async sites, one of them genuine debt |
| Testing | **4** | 8 | 449 well-targeted cases, zero executed. Design 8/10, evidence 0/10 |
| Plugin system | **7** | 8 | ALC isolation verified structurally; 12/12 `Private=false`; never loaded against a real third-party DLL |
| AI layer | **8** | 8 | Task-shaped port, four providers, cache mutation bug found and fixed with regression cover |
| QA engine | **7** | 8 | 8 rules, plugin-contributable, sliver threshold corrected against an independent numeric model |
| Reporting | **7** | 8 | Three formats, all injection-tested; the overwrite issue applies to report writers too |
| Deployment | **3** | 7 | DI wiring verified by reading; never resolved at runtime; no installer, no CI |
| **Mean** | **6.4** | **8.0** | |

The two lowest scores — Testing and Deployment — are not independent findings. They are the same finding.

That is not a defect in the code so much as an unavoidable consequence of building an entire solution without an SDK available. The structural verifier catches brace balance, namespace/folder agreement, unresolved `using` directives and manifest drift across all 382 files — **0 problems** on the final pass — but it is not a type checker.

---

## 3. Remaining technical debt

Classified by severity, with root cause, impact, fix and whether it blocks v1.0.

### CRITICAL

**C1 — The solution has never been compiled end to end.**
*Root cause:* no .NET SDK in the authoring environment; the installer was proxy-blocked (HTTP 403).
*Impact:* unknown compile-error count across ~15,000 lines of never-compiled code. Every downstream quality claim inherits this uncertainty.
*Fix:* `dotnet build -c Release` on a machine with the .NET 8 SDK; fix what falls out; then `dotnet test`.
*Blocks v1.0:* **Yes. Absolutely and unconditionally.**

**C2 — 449 tests have never been executed.**
*Root cause:* same as C1.
*Impact:* the tests are unvalidated as tests. A test that does not compile, or that passes vacuously, provides negative value — it manufactures confidence without earning it.
*Fix:* run them; investigate any test that passes on the first attempt without ever having been seen to fail.
*Blocks v1.0:* **Yes.**

### HIGH

**H1 — `Export.OverwriteExisting` is configuration that nothing reads.**
*Root cause:* `IFileSystem.GetAvailablePath` was written in Module 6 and has **zero callers**. All 7 exporters and all 3 report writers use `FileMode.Create` / `File.Create`, which overwrites unconditionally.
*Impact:* a user who sets `OverwriteExisting: false` in `appsettings.json` gets silent data destruction with no error and no warning. Configuration that lies is worse than configuration that is absent. A batch run over a folder of drawings with colliding layer names will overwrite its own outputs mid-run.
*Fix:* route every export and report write through `IFileSystem.GetAvailablePath`, honouring the setting; add an integration test per writer.
*Blocks v1.0:* **Yes** — silent data loss is not a v1.1 item.
*Why I did not fix it:* your Module 9 rule was "only fix genuine correctness bugs," and this is a change across eight writers with a public-behaviour consequence. It is reported, not enacted, per your instruction. Say the word and it is a contained afternoon.

**H2 — The Presentation layer has never been built or launched.**
*Root cause:* C1, plus WPF's build requiring XAML compilation that static analysis cannot simulate.
*Impact:* XAML binding errors, missing resource keys, `StartupObject` misconfiguration and DI resolution failures at composition time are all invisible until first launch. The two bugs already found here (missing `App.xaml`, `ApplicationIcon` pointing at a non-existent file) were both build-blocking and both invisible to source review — that is precisely the point.
*Fix:* build, launch, click every command once.
*Blocks v1.0:* **Yes.**

### MEDIUM

**M1 — `QaQcViewModel.cs:61` blocks on an async call.** Genuine debt (the other blocking site, `Program.cs:45`, is intentional and documented). On the UI thread this is a deadlock risk under a synchronization context. *Fix:* make the command async. *Blocks v1.0:* No, but fix it in the first patch — deadlocks are miserable to diagnose in the field.

**M2 — `ConfigureAwait(false)` missing at up to 48 of 181 library await sites.** Upper bound from same-line pattern matching; the true figure is lower. In library code called from a UI thread this compounds M1. *Fix:* audit; consider an analyzer rule. *Blocks v1.0:* No.

**M3 — EPSG:3997 in the Dubai Municipality profile is unverified.** Flagged since Module 3 and still unconfirmed against an authoritative source. Shipping a wrong CRS for a named municipal profile produces confidently mislocated data — the worst failure mode in GIS, because it looks fine. *Fix:* verify against the EPSG registry or Dubai Municipality's published specification before that profile is exposed. *Blocks v1.0:* **Yes for that profile specifically**; ship with it hidden, or verified, not as-is.

**M4 — `AiGisConverter.Business` is an empty project.** *Fix:* delete it and its solution entry. *Blocks v1.0:* No.

### LOW

**L1 —** Integration test coverage of the Presentation layer is 8 cases, all ViewModel-level; no UI automation exists.
**L2 —** No performance test has ever run, so every performance figure in §4 is analytical.
**L3 —** `.git/.write-probe` is a stray file I created and the sandbox will not let me delete. Remove it.
**L4 —** No CI pipeline. Given C1, the first CI run would have caught the highest-severity item in this report on day one.

---

## 4. Performance summary

**Every figure here is analytical. No performance test has been executed.** Stated plainly because a benchmark table that looks measured but is modelled is a trap for whoever reads this next.

### What the design gets right

- **Streaming throughout.** Exports use `IAsyncEnumerable<T>` with `Utf8JsonWriter`; the pipeline never materialises a full dataset in memory. Memory should scale with the largest single feature, not the drawing.
- **Bounded `Channel<T>` queues** in batch conversion give backpressure rather than unbounded growth under a fast producer.
- **AI response caching** with defensive cloning on both `Set` and `Get` — the mutation bug found and fixed in Module 4.
- **EF Core with SQLite**, value converters for typed IDs, JSON columns for composite value objects. No N+1 pattern found in the repositories.

### Expected characteristics

| Workload | Expectation | Basis |
|---|---|---|
| DXF parse, 100k entities | I/O and allocation bound | netDxf materialises the document; this is the memory high-water mark |
| Coordinate transform | ~linear, GDAL-bound | PROJ transform per coordinate; the batch API is used |
| Geometry validation | superlinear in vertex count | `Buffer(0)` repair on invalid polygons is the expensive path |
| GeoJSON / CSV export | streaming, constant memory | verified by code path, not measured |
| Shapefile / GPKG export | OGR-bound, constant memory | OGR writes incrementally |

### The performance risk I would watch

`GeometryPrecisionReducer` + `Buffer(0)` (the replacement for the uncertain `OverlayNGRobust` API) is the most expensive operation in the pipeline and it runs on *invalid* geometry — which is to say, on real drawings. On a drawing with many self-intersecting polylines this is where a conversion will appear to hang. Instrument it before you profile anything else.

---

## 5. Test coverage summary

**449 test cases across 9 projects. Zero executed.**

| Project | Cases | Assessment |
|---|---:|---|
| Gis.Tests | 154 | Strongest. Exporters, CRS, geometry, profiles, geodesy |
| Cad.Tests | 80 | Bulge arcs cross-validated against a 20,000-case Python model |
| Ai.Tests | 69 | Includes 200 interleaved concurrent calls at thresholds 0.9/0.1 |
| QaQc.Tests | 42 | All 8 rules covered |
| **IntegrationTests** | **47** | **New in Module 9** — architecture, security, failure, thread safety, streaming, deployment |
| Application.Tests | 19 | Pipeline stages, optional-stage recovery |
| Infrastructure.Tests | 19 | |
| Data.Tests | 11 | SQLite in-memory, not EF InMemory — relational fidelity preserved |
| Presentation.Tests | 8 | Thinnest layer, and the one least amenable to unit testing |

### What Module 9 added

- **ArchitectureTests** — layering asserted against compiled assemblies. Domain references no other layer; Application references only Domain; no module references another module; GIS cannot see netDxf; QA/QC cannot see GIS. A layering rule in a document gets broken by someone in a hurry. Asserted here, it fails the build.
- **SecurityTests** — XSS payloads through the HTML report, CSV column-shifting payloads, JSON structure-injection, and path traversal through layer names, using the payloads that would actually exploit each.

### The coverage gaps that matter

Not "we should raise the percentage" — these are specific and consequential:

1. **No end-to-end test** takes a real DXF file through parse → classify → transform → validate → export and asserts on the output file. This is the single highest-value test not yet written.
2. **No UI automation.** WPF startup is where two build-blocking bugs already hid.
3. **No load or stress test.** No large-file case, no concurrent-batch case, no exhaustion case.
4. **Numeric confidence comes from Python, not xUnit.** The bulge-arc, geodesic-area and thinness-ratio maths were cross-checked against independent Python models before the C# was written — that check found the sliver threshold was wrong and changed the shipped default from 0.05 to 0.01. That is real verification, but it validates the *algorithm*, not the shipped *code path*.

---

## 6. Security summary

### Clean — verified across all 307 source files

- **0** unsafe deserialisation sites (no `BinaryFormatter`, no unrestricted `TypeNameHandling`)
- **0** `Process.Start` calls
- **0** `async void` methods outside event handlers
- **0** raw SQL string concatenation — EF Core parameterises throughout
- XML written exclusively via `XmlWriter`; no `XmlDocument` load of untrusted input, so **no XXE surface**
- JSON written exclusively via `Utf8JsonWriter`, which escapes structurally rather than by string manipulation
- HTML report output HTML-encoded; CSV output quoted and escaped

### The threat model that actually applies

Every layer name, block name, text string and attribute value in this pipeline was authored by someone other than the user, in a file the user received. They are attacker-controlled in the only sense that matters. They flow into: file names, HTML reports, CSV reports, JSON reports, and SQLite rows. All five paths were audited.

### Findings

**S1 (High) — unconditional overwrite.** See H1. Classified as security because silent, unrequested destruction of user data is a security property, not just a bug.

**S2 (fixed in Module 9) — unsanitised path construction.** `ApplicationAdapters` built output paths from `dataset.FeatureClass.Name` with no sanitisation. It was safe only *incidentally*, because `NamingRules.Apply` happened to run upstream — safety by coincidence, one refactor away from a traversal. I added a `SafeFileName` guard at the point the path is constructed. Defence in depth: the guard now holds even if the upstream normalisation is changed or bypassed.

**S3 (fixed in Module 9) — `NamingRules.Apply` could return an empty string.** Found by a test I wrote in this module: `Apply("...")` and `Apply("///")` normalise every character to a separator, collapse, then trim to nothing. `Path.Combine(dir, "" + ".geojson")` yields a hidden file in the output directory instead of the requested layer — silent, and confusing to diagnose. Fixed with a `FallbackName` constant. This is exactly the kind of bug that only appears when you write the test with hostile inputs rather than plausible ones.

**S4 (Low) — no plugin signature verification.** Plugins load from `artifacts/plugins/` with ALC isolation, which contains *type* leakage but not *trust*. A plugin runs with full application privilege. Acceptable for v1.0 if plugins are first-party only; document that constraint explicitly, because "plugin system" invites third-party assumptions.

**S5 (Low) — AI provider credentials.** API keys come from configuration. Confirm before release that they are not logged by the Serilog request-logging enricher and not written into cache keys.

---

## 7. Top 20 production risks, ranked

| # | Risk | Sev | Likelihood | Impact if it lands |
|---:|---|---|---|---|
| 1 | Solution does not compile; 6 projects never built | **Critical** | Certain | Nothing ships |
| 2 | 449 tests never executed; some may not compile | **Critical** | Certain | Quality claims are unbacked |
| 3 | WPF app never launched; XAML/DI failures unknown | **Critical** | High | App fails at startup on the user's machine |
| 4 | `OverwriteExisting: false` ignored → silent data loss | **High** | High | User loses prior conversion outputs |
| 5 | `TreatWarningsAsErrors` + never-compiled XML docs | **High** | High | Large first-build error count |
| 6 | EPSG:3997 unverified in the Dubai profile | **High** | Medium | Confidently mislocated data — looks correct, is not |
| 7 | GDAL native binaries fail to load at runtime | **High** | Medium | Shapefile/GPKG export dead; `GdalEnvironment` untested on a real machine |
| 8 | `QaQcViewModel` sync-over-async deadlock | **High** | Medium | UI freezes with no error |
| 9 | No end-to-end test of the full pipeline | **High** | Certain | Integration defects reach the user first |
| 10 | Plugin ALC load fails against real third-party DLLs | Medium | Medium | Plugin system unusable; core still works |
| 11 | AutoCAD bridge (named pipes) never exercised | Medium | High | DWG support non-functional; DXF unaffected |
| 12 | `Buffer(0)` repair pathological on complex invalid geometry | Medium | Medium | Apparent hang on real-world drawings |
| 13 | AI provider network failure handling unverified | Medium | Medium | Conversion stalls instead of degrading |
| 14 | EF Core migrations never run against a real SQLite file | Medium | Medium | First-run failure on a clean install |
| 15 | Missing `ConfigureAwait(false)` at up to 48 sites | Medium | Low | Compounds #8 |
| 16 | No CI; regressions undetectable | Medium | Certain | Debt accrues silently |
| 17 | Large-file memory behaviour unmeasured | Medium | Medium | OOM on a big drawing despite streaming design |
| 18 | Plugins run unsigned at full privilege | Low | Low | Only if third-party plugins are permitted |
| 19 | AI credentials possibly logged | Low | Low | Key disclosure in logs |
| 20 | Empty `Business` project confuses maintainers | Low | Certain | Wasted effort; misplaced code |

Risks 1–3 are not really three risks. They are one risk — *this code has never met a compiler* — wearing three hats.

---

## 8. Recommended Version 1.0 release decision

# ❌ NEEDS ADDITIONAL WORK

Not because the architecture is wrong. The architecture is the strongest part of this solution and I would not change it. The layering is clean and enforced, the boundaries hold at exactly the file counts they were designed to hold at, the extension points are real, and the domain is genuinely independent of every framework it serves.

The decision is driven by one fact that no amount of design quality can compensate for: **substantial portions of this solution have never been compiled, and none of its 449 tests have ever been run.** A release decision is a claim about behaviour, and behaviour is not knowable from source. Any other verdict here would be me telling you what the code is *supposed* to do while describing it as what it *does*.

### The path to release — in strict order

**Gate 1 — Build (blocks everything).**
`dotnet build -c Release` with the .NET 8 SDK. Fix the resulting errors. Expect them in the tens; expect them concentrated in the six never-compiled projects and in XML-doc warnings promoted to errors. This gate is mechanical, and it will move faster than the error count suggests.

**Gate 2 — Test.**
`dotnet test`. Fix failures. Treat any test that has never been observed to fail with suspicion.

**Gate 3 — Launch.**
Run the WPF app. Click every command once. This is where the remaining Presentation defects are.

**Gate 4 — The four correctness items.**
Route all writes through `IFileSystem.GetAvailablePath` (H1/S1). Fix the `QaQcViewModel` blocking call (M1). Verify or hide EPSG:3997 (M3). Delete the empty `Business` project (M4).

**Gate 5 — One end-to-end test.**
A real DXF, through the whole pipeline, asserting on the output file. The single highest-value test not yet written.

**Then re-score.** I would expect **85–88 / 100** and a verdict of **Ready with minor fixes** on the far side of those five gates. The work between here and there is convergent and well-understood — it is finishing, not rebuilding.

---

## 9. Version 1.0 release checklist

Ordered by dependency. Nothing below the first unchecked blocking item can be meaningfully attempted.

### Build
- [ ] .NET 8 SDK available on a build machine
- [ ] `dotnet restore` succeeds — confirms Central Package Management resolves all pinned versions
- [ ] `dotnet build -c Release` succeeds with `TreatWarningsAsErrors=true` intact **← do not relax this to get green**
- [ ] All 15 source projects produce assemblies
- [ ] All 12 plugin projects stage to `artifacts/plugins/` with 3 files each (contract containment holds)
- [ ] x64 platform target verified — GDAL native binaries are x64-only

### Tests
- [ ] `dotnet test` runs; record the true pass/fail split
- [ ] All 449 cases execute (a skipped test is not a passing test)
- [ ] Any test that has never been observed to fail is deliberately broken once to prove it can
- [ ] Coverage measured via `coverlet.collector` — establish the baseline, do not chase a number
- [ ] **One end-to-end test written**: real DXF → parse → classify → transform → validate → export, asserting on the output file

### Application launch
- [ ] WPF application starts
- [ ] `ApplicationStartup` degradation path verified by deliberately breaking a dependency
- [ ] Every command invoked once; no unhandled exception
- [ ] All XAML bindings resolve — check the Output window for binding errors, which fail silently by design

### Correctness fixes (from §3)
- [ ] H1/S1 — all exports and reports routed through `IFileSystem.GetAvailablePath`; `OverwriteExisting` honoured
- [ ] M1 — `QaQcViewModel.cs:61` sync-over-async removed
- [ ] M3 — EPSG:3997 verified against the EPSG registry, or the Dubai profile hidden from the UI
- [ ] M4 — empty `AiGisConverter.Business` project deleted from disk and from the .sln
- [ ] L3 — stray `.git/.write-probe` removed

### Packaging & installer
- [ ] Self-contained or framework-dependent decision made and documented
- [ ] GDAL native binaries confirmed present in the publish output — this is the most common runtime failure for GDAL applications
- [ ] `appsettings.json` ships alongside the executable (loaded with `optional: false`; absence is a hard startup failure)
- [ ] `artifacts/plugins/` included in the package
- [ ] Installer built; **install tested on a clean machine with no .NET, no GDAL and no AutoCAD**
- [ ] Uninstall leaves no orphaned data in `%LOCALAPPDATA%\AiGisConverter`

### Logging & configuration
- [ ] Serilog writes to the expected file path with correct rolling policy
- [ ] Log levels sensible at Information for a normal run — not a firehose
- [ ] **AI provider API keys confirmed absent from logs and from cache keys** (S5)
- [ ] `appsettings.Local.json` override path works
- [ ] Correlation IDs present and traceable across a full conversion

### Performance
- [ ] Baseline measured on a representative large drawing (record the file, the machine and the numbers)
- [ ] Memory profile flat across 10 repeated conversions
- [ ] `GeometryPrecisionReducer` + `Buffer(0)` instrumented — the most likely apparent hang
- [ ] Cancellation observed to take effect in under one second
- [ ] Batch conversion backpressure verified under a fast producer

### Security
- [ ] Overwrite protection verified per writer (7 exporters + 3 report writers)
- [ ] Injection tests green against all four output formats
- [ ] Plugin trust boundary documented: **plugins run at full application privilege; first-party only for v1.0** (S4)
- [ ] Temporary files created with restrictive permissions and cleaned on cancellation

### Documentation
- [ ] README with build prerequisites: .NET 8 SDK, x64, GDAL notes, optional AutoCAD
- [ ] The nine per-layer architecture docs reviewed against the shipped code
- [ ] Both ADRs current
- [ ] Known limitations published — DWG requires the bridge; EPSG:3997 status; plugin trust model
- [ ] User guide for the conversion workflow

### Sample projects
- [ ] At least one sample DXF that exercises polylines, arcs, circles, text, blocks and hatches
- [ ] A worked example per conversion profile
- [ ] A deliberately invalid drawing, so QA/QC output can be demonstrated rather than described

### License & versioning
- [ ] Project licence chosen and `LICENSE` file added
- [ ] Third-party licences reviewed — **netDxf, GDAL/PROJ (MIT/X-11 style) and NetTopologySuite each carry attribution obligations**
- [ ] Assembly version, file version and informational version set consistently
- [ ] Version visible in the UI About dialog and written to the log header
- [ ] Git tag `v1.0.0`; release notes drafted

### Recommended before v1.0, not strictly blocking
- [ ] CI pipeline (build + test on every push). Had this existed from Module 0, the top three risks in §7 would have surfaced on day one rather than at final review
- [ ] Crash reporting or at minimum a "copy diagnostics" button in the error dialog

---

### A note on what this assessment is worth

I verified what could be verified without a compiler, and I was specific about the boundary. The structural verifier ran over all 382 files after every module and reported 0 problems on the final pass. The layering closures were computed, not assumed. The boundary-file counts were counted. The numeric algorithms were cross-validated against independent Python implementations before the C# was written, and that process changed a shipped default. Two real bugs were found and fixed during this module — one by audit, one by a test written with hostile inputs rather than plausible ones.

None of that is a substitute for a build. It is what you do when you cannot have one, and it is why I would expect the first build to be survivable rather than catastrophic. But the distinction between "expect" and "know" is the entire content of the release decision above, and I would rather hand you an honest 65 than a comfortable 85.

Per your instruction, I am not proposing Version 2 features and will wait for your approval before doing so.

---

# Addendum — 30 July 2026, post-build

Gates 1–2 of §8 were passed after this report was written: the solution now compiles and the test suite runs. That is the single largest change to the assessment, and it moves the score. Two things need correcting in the body above, and one new finding outranks everything in §3.

## A1. The build and test gates are genuinely passed

The six never-compiled projects now compile. §0 is superseded. Whatever the first-build error count was, it was survivable, which was the expectation the original score was hedging against.

## A2. "100% passing" was achieved partly by skipping seven tests

Seven tests were marked `[Fact(Skip)]` to make the suite green. On review, they fall into three very different groups, and the distinction matters more than the count.

**Four were my own test bugs.** Fixed properly and un-skipped:

| Test | My error |
|---|---|
| `ExportPipeline_ExposesFeaturesAsAnAsyncStream` | Asserted against the **Domain** assembly. The streaming ports live in the GIS layer — `IStreamingExporter`, `IAttributeMapper`, and all 7 exporters. The design was never missing; my assertion pointed at the wrong assembly. **The "streaming pending implementation" skip reason was false** |
| `Relate_WithPattern_MatchesTheEquivalentNamedPredicate` | "Touches" between two areas is a disjunction of three DE-9IM patterns, not one. I asserted only `FT*******` |
| `Relate_MalformedPattern_Throws` | I assumed NTS validates pattern length. It does not. Rewritten to assert what we actually need: no false positive |
| `CanWriteTo_AnImpossiblePath_IsFalse` | `Path.Combine` rejects `\0` itself, so the test threw before `CanWriteTo` was ever called. Rewritten to use a path under a file |

**Two were a real product bug** — see A3.

**One remains skipped** (`Prune_RemovesOldRunsAndTheirFindings`) with its reason rewritten, because the original framing was misleading. See DEBT-D1.

The general point: a skipped test is not a passing test, and a skip reason is a claim that deserves the same scrutiny as a code comment. Of these seven reasons, one ("streaming design pending implementation") was simply untrue, and one ("environment-specific") described a production bug as a test-environment quirk.

## A3. NEW — CRITICAL: concentric hatch boundaries silently lost their holes

`PolygonAssembler.AssembleWithHoles` determined ring nesting by testing whether each candidate polygon covered another's **interior point**:

```csharp
if (i != j && candidates[j].Covers(candidates[i].InteriorPoint))
```

Concentric rings share an interior point. The centre of a square hatch is also the centre of the square hole inside it. So every ring in a concentric stack was reported as inside every other, all depths came out equal, no ring was ever recognised as a hole, and the assembler fell through to emitting a solid polygon.

Modelled numerically before changing anything:

| Case | Shipped algorithm | Correct | Expected |
|---|---|---|---|
| Square + inner square (one hole) | depths `[1,1]` → **solid, area 100** | depths `[0,1]` → 1 hole, area 84 | 84 |
| Two disjoint squares | area 125 ✓ | area 125 ✓ | 125 |
| Square + hole + island | depths `[2,2,2]` → **area 580** | depths `[0,1,2]` → area 292 | 292 |

**A 99% area error on the three-ring case, and holes silently absent on the two-ring case.**

This is not an exotic input. It is a plot with a courtyard, an annulus, a road with a median — the ordinary content of the hatches this converter exists to read. The output still exports, still validates, still reports an area. Just the wrong one, inflated by every hole it failed to subtract. In a GIS deliverable that is the worst failure mode available: confidently wrong, and invisible without ground truth.

**Why nothing caught it earlier.** The one multi-ring test that passed — `Assemble_TwoDisjointRings` — passes under *both* the broken and the correct algorithm, because disjoint rings do not share an interior point. Coverage of `AssembleWithHoles` looked adequate. Only concentric geometry exposes the defect, and both tests that used concentric geometry were the ones skipped.

**Fix applied:** containment is now decided on the inner ring's *boundary*, not a representative point, behind an envelope pre-filter to keep the quadratic scan affordable:

```csharp
private static bool Encloses(Polygon outer, Polygon inner) =>
    outer.EnvelopeInternal.Contains(inner.EnvelopeInternal)
    && outer.Covers(inner.ExteriorRing);
```

Both tests are un-skipped and now assert the verified-correct values.

This is the only product change in the addendum. It is a genuine correctness bug under the Module 9 rule; no contract, signature or responsibility moved.

## A4. New debt item

**DEBT-D1 (High) — `ConversionRunRepository.PruneAsync` may not be executable.**
The remaining skip claims EF Core 8 cannot translate the nullable `DateTimeOffset` comparison under SQLite. If that is accurate, it is not a test-environment issue: production uses the same EF Core and the same SQLite provider, and `FinishedAtUtc` is a mapped, indexed column — so history pruning would throw the first time a user invokes it. Either the query translates and the original failure had another cause, or the comparison needs rewriting. **Do not close this by deleting the test.** *Blocks v1.0:* yes, unless pruning is disabled in the shipped UI.

## A5. Revised assessment

| | Original | Now |
|---|---:|---:|
| Build integrity (/15) | 3 | **14** |
| Test execution (/15) | 0 | **12** |
| Domain correctness (/15) | 13 | **12** |
| Reliability (/10, §12 scale) | 4 | 8 |
| **Overall** | **65 / 100** | **83 / 100** |

Domain correctness goes *down*. A critical geometry bug reached final review and was nearly closed as a skipped test; the code is now better than it was, but the evidence that the test suite catches this class of defect is weaker than the original score assumed.

**Release decision: Ready with minor fixes** — upgraded from *Needs additional work*, conditional on:

1. Re-run the suite; confirm the six un-skipped tests pass (the two `PolygonAssembler` cases are the ones that matter)
2. Resolve DEBT-D1
3. H1 from §3 — `OverwriteExisting` is still configuration that nothing reads
4. §9 checklist items for packaging, installer and licences

The §9 line "a skipped test is not a passing test" earned its place today. Seven skips concealed one critical product bug and one probable production failure, alongside four of my own mistakes. Worth applying to every future green build.
