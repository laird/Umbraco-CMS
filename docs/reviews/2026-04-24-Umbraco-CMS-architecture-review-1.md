# Umbraco CMS — Architecture Review

- **Date**: 2026-04-24
- **Branch**: `planfirst` (worktree at main HEAD `31dfd52b`)
- **Version**: 17.5.0-rc (from `version.json`)
- **Reviewer scope**: Repository-level architecture; no runtime traffic observed.

---

## Summary

Umbraco CMS at v17.5 is a mature, layered .NET 10 monolith-with-pluggable-packages. The architecture is internally consistent: Core defines contracts, Infrastructure implements them, Web/APIs consume. Binary-compat discipline (Obsolete + `StaticServiceProvider` + default interface methods) is rigorously applied — 705 `[Obsolete]` attributes, 109 `TODO (V…)` removal markers, 279 `StaticServiceProvider` call sites — which is simultaneously the codebase's biggest strength (no breaking changes within a major) and its single largest long-term risk (ossification, DI opacity). The biggest concrete risk right now is **concurrent NPoco + EF Core persistence** with no published cutover plan; schema, migration, and transaction semantics must stay aligned across two ORMs indefinitely until a decision is made. Other notable risks: very large domain services (`ContentService.cs` = 4,093 lines, `IContentService.cs` = 730 lines) that resist safe refactoring, a 1.4 MB generated `OpenApi.json` committed and manually refreshed, and known lock-table contention in distributed background jobs (already researched but unresolved).

**Pillar maturity (1–10):**

| Pillar | Score | One-line justification |
|---|---|---|
| Architecture & Design | **8** | Clean layered design, consistent patterns (Composer, Notification, Scope, Attempt); weakened by very large aggregate services. |
| Reusability & Extensibility | **9** | Interface-first, Composer DI, notification system, and HybridCache + Examine plug points are excellent; backoffice extension API matches. |
| Data Architecture | **6** | Dual ORM (NPoco + EF Core) in flight; lock-table contention on small shared table; mature migrations engine offsets risk. |
| Performance & Capacity | **6** | HybridCache is a real improvement; sync-over-async prevalence (>500 `.Result`/`.Wait()` sites) and lock-table contention limit LB-scale headroom. |
| Operations & Production Readiness | **7** | Strong obs (Serilog, MiniProfiler), explicit LB requirements, healthchecks, but no production Dockerfile at root and thin deployment docs. |

No prior reviews exist in `docs/reviews/`.

---

## Architecture Walkthrough

**Layering (enforced by project references in `umbraco.sln`):**

```
Web.UI → Web.Common → Infrastructure → Core
              ↓
        Api.Management ─┐
        Api.Delivery  ──┼→ Api.Common → Web.Common → Infrastructure → Core
        Api.Common    ──┘
Pluggable: PublishedCache.HybridCache, Examine.Lucene, Persistence.{EFCore*,Sqlite,SqlServer}, Imaging.ImageSharp{,2}
```

**Critical request path (Delivery API, anonymous GET):**
`Kestrel → middleware (routing, auth) → Api.Delivery controller → ICacheManager (Content/Media/Members/Domains) → HybridCache (L1 in-memory + L2 distributed if configured) → fallback to repository via IScopeProvider → NPoco or EF Core ↓ SQL Server/SQLite`.

**Critical request path (Management API, authenticated):**
`Kestrel → OpenIddict token cookie validation (reference tokens stored server-side, cookie name `__Host-*`) → ASP.NET Data Protection → controller → service (Core/Infrastructure) → Scope (Unit of Work) → repository → DB`.

**State management:**
- Per-request scope via `IScopeProvider` / `ICoreScopeProvider` (`src/Umbraco.Infrastructure/Scoping/Scope.cs`, 663 lines).
- Locking via `umbracoLock` table rows (-331…-348). `Constants.Locks.DistributedJobs = -347`.
- Distributed cache refresh via `DatabaseServerMessenger` — polls `umbracoCacheInstruction`, broadcasts to `CacheRefresher` implementations (~45 in Core).
- Token storage: OpenIddict reference tokens in DB + secure HTTP-only cookie on the client (v17 change).

**Trust boundaries:**
1. Public (Delivery API, rendered website) — anonymous-capable, CORS-bounded.
2. Backoffice (Management API + UI.Client SPA) — OpenIddict/OIDC, cookie-bound tokens.
3. Server-to-server (distributed cache messenger, background jobs) — shared DB as bus.

**Implicit decisions worth surfacing:**
- **Database is the message bus** (`umbracoCacheInstruction`, `umbracoServer`, `umbracoLock`). This is simple and avoids a separate broker, but couples load-balancing scalability to DB page-level lock behavior (see Finding D-1).
- **Two ORMs live side by side** — NPoco for the legacy repository layer; EF Core being introduced under `src/Umbraco.Cms.Persistence.EFCore*`. No cutover schedule found in repo.
- **OpenAPI spec is a committed artifact** (`src/Umbraco.Cms.Api.Management/OpenApi.json`, 1.4 MB) refreshed manually per PR, not at build time.
- **`StaticServiceProvider` is load-bearing** for back-compat (see Finding R-1) — 279 call sites, mostly in obsolete ctors/default interface methods.

**What would need review in a real engagement but wasn't visible from repo alone:**
- Live query plans and hot-path execution profiles against a realistic dataset.
- Real packet capture of the distributed cache messenger under multi-node load.
- Tenant/site scale data (content node counts, concurrent editor counts, request volumes).
- Database sizing and index coverage beyond what migrations declare.

---

## Findings

### 1. Architecture & Design Quality

#### A-1. Several domain services have grown into aggregates that resist refactoring
- **Evidence**: `src/Umbraco.Core/Services/ContentService.cs` = 4,093 lines; `src/Umbraco.Core/Services/IContentService.cs` = 730 lines; `src/Umbraco.Infrastructure/Scoping/Scope.cs` = 663 lines. `Services/` in Core has 251 `.cs` files and 130 interfaces.
- **Impact**: New capabilities require edits near the center of the interface, each with a binary-compat ripple (new method + default impl via `StaticServiceProvider`). Risk of silently introducing regressions grows with surface area. Test isolation becomes harder.
- **Severity**: **High**.
- **Recommendation**: Treat `IContentService` as a meta-interface and introduce explicit read/write slices (e.g., `IContentReadService`, `IContentPublishingService`, `IContentRelationService`) behind the same class (or small adapters). New methods land on the slice; `IContentService` absorbs them via default interface methods. This is an additive refactor compatible with v18/v19 and gives future deprecation lines clean seams.

#### A-2. Interface-first design + Composer pattern is a genuine strength
- **Evidence**: 681 `I*.cs` files in `src/Umbraco.Core`, `Composing/` pattern is project-wide, 255 notification types in `src/Umbraco.Core/Notifications/`.
- **Impact**: Extensibility ceiling is high; third parties can replace nearly any seam.
- **Severity**: **Strength**.
- **Recommendation**: Preserve the invariant. Reject PRs that introduce Infrastructure-only abstractions that could live in Core.

#### A-3. Notification system as the primary side-effect channel is well-suited to the domain but under-documented for contributors
- **Evidence**: 255 notification types in `Notifications/`; `CLAUDE.md` explains the preference over C# events, but the *ordering* and *transactionality* of notifications vs. scope completion is only implicit in `Scope.cs`.
- **Impact**: External package authors can subscribe to a `*ing`/`*ed` pair without realizing the `ed` notification fires at scope completion (or that a rolled-back scope suppresses it). Creates subtle bugs.
- **Severity**: **Medium**.
- **Recommendation**: Add a short "Notification lifecycle" section to `src/Umbraco.Core/CLAUDE.md` stating (a) when handlers run relative to `scope.Complete()`, (b) what happens on rollback, (c) cancelability semantics, with a link to the specific lines in `Scope.cs` / notification publishing code.

### 2. Reusability & Extensibility

#### R-1. `StaticServiceProvider` is load-bearing for back-compat and has become a shadow DI container
- **Evidence**: 279 usages across `src/`. Idiom is documented in `CLAUDE.md` §5 for obsolete ctor + default interface methods. Many of these will never be removed on schedule because upstream consumers bind to the obsolete ctor.
- **Impact**: Service resolution is now partially ambient; testability of any class using the obsolete-ctor delegation path requires a real DI container or a `StaticServiceProvider` harness. Makes it hard to reason about graph construction.
- **Severity**: **High**.
- **Recommendation**: Introduce a lightweight analyzer (or roslyn analyzer rule in `.globalconfig`) that flags *new* `StaticServiceProvider` call sites outside `[Obsolete]` delegation paths. Add a per-major-version dashboard counting `StaticServiceProvider` references; the number should be flat or declining, not growing.

#### R-2. `[Obsolete]` discipline is unusually rigorous — both strength and backlog
- **Evidence**: 705 `[Obsolete]` attributes, 109 `TODO (V…)` removal markers. Each includes removal schedule per `CLAUDE.md` §5.
- **Impact**: Strong external API stability, but each major release inherits an irremovable pile unless a ruthless culling step exists. Without a "removal PR" gate at major-version freeze, these become permanent.
- **Severity**: **Medium**.
- **Recommendation**: Script a pre-major-freeze check: `grep -rn "Scheduled for removal in Umbraco 19"` (substitute target) produces a list; a single "v19 deprecation cleanup" PR must close this list before v19 GA. Add the check to the release checklist (`RELEASE_INSTRUCTION.md`).

#### R-3. Dual persistence API (NPoco + EF Core) without a stated cutover
- **Evidence**: Both `Umbraco.Cms.Persistence.Sqlite/.SqlServer` (NPoco) and `Umbraco.Cms.Persistence.EFCore.Sqlite/.SqlServer` ship in v17. `CLAUDE.md` §6 says both are "fully supported". No roadmap for EF Core parity or legacy retirement in repo.
- **Impact**: Two transaction systems must stay consistent (same `umbracoLock` rows, same migration engine). Any future bug in scope/transaction semantics needs to be fixed twice. Third-party repository authors must pick an ORM and bet on it.
- **Severity**: **High**.
- **Recommendation**: Publish a cutover ADR in `docs/`. At minimum: (a) define the target state (one ORM, or explicit coexistence with a boundary), (b) list which repositories have EF Core implementations today, (c) commit to a version where new repositories MUST use EF Core. Without this, v18/v19 decisions will be made case-by-case.

### 3. Data Architecture

#### D-1. `umbracoLock` table contention is a known scalability ceiling
- **Evidence**: `research-load-balanced-distributed-jobs.md` (issue #22113): the ~18 rows of `umbracoLock` fit on a single 8 KB SQL Server data page; `WITH (REPEATABLEREAD)` table hint causes page-level locks; default 5s write-lock timeout.
- **Impact**: Any long-running scope holding a row lock blocks unrelated background jobs. Fails predictably under sustained editor load in distributed deployments. Already observed in the wild.
- **Severity**: **High**.
- **Recommendation**: Three layers: (1) add `WITH (ROWLOCK, REPEATABLEREAD)` hint to the lock acquisition SQL to force row-level granularity; (2) pad `umbracoLock` rows with a fixed-size filler column so rows spill across multiple pages; (3) raise `DistributedJobSettings` write-lock default from 5s to something reasoned (20–30s) and document it. (1) and (2) are independently useful — (2) guarantees isolation even if the optimizer ignores the hint on large scans.

#### D-2. Database-as-message-bus couples cache fan-out latency to DB polling cadence
- **Evidence**: `DatabaseServerMessenger` polls `umbracoCacheInstruction` (see `research-memory-leaks.md` Finding 1 for related code).
- **Impact**: Cache invalidation fan-out latency = polling interval (default seconds). Content edits on one node may be stale on others until the next poll. Under high instruction volume, instruction table grows and poll cost rises.
- **Severity**: **Medium** (by design; only becomes High at high node counts).
- **Recommendation**: Document the latency contract clearly in `CLAUDE.md` and hosting docs. For >3-node deployments, consider an optional push transport (SignalR or Redis pub/sub) behind `IServerMessenger`, re-using the existing abstraction.

#### D-3. Schema evolution via `MigrationPlan` is mature and well-abstracted
- **Evidence**: `src/Umbraco.Infrastructure/Migrations/` has explicit plan, executor, expressions, async migration, post-migrations, and a `MergeBuilder` for reconciling divergent upgrade paths.
- **Impact**: Upgrades across majors remain tractable.
- **Severity**: **Strength**.
- **Recommendation**: Retain. Ensure EF Core migrations sit inside the same plan model (not separate `Update-Database` workflow) so a single upgrade runs both.

#### D-4. `OpenApi.json` as a 1.4 MB committed artifact with manual refresh
- **Evidence**: `src/Umbraco.Cms.Api.Management/OpenApi.json` = 1.4 MB. `CLAUDE.md` §6 documents a manual copy-from-Swagger-UI workflow.
- **Impact**: Drift is likely; PR diffs are noisy; formatting churn from IDEs is explicitly called out in `CLAUDE.md`. Review quality suffers.
- **Severity**: **Medium**.
- **Recommendation**: Either (a) generate it at build time via `Swashbuckle.AspNetCore.Cli` in a CI job and fail if the checked-in file diverges from the generated one, or (b) stop committing it and publish as a build artifact. (a) is lower risk — consumers that depend on the file still see no change.

### 4. Performance & Capacity

#### P-1. Sync-over-async patterns in hot paths
- **Evidence**: 405 `.Result`/`.Wait()`/`.GetAwaiter().GetResult()` sites in `src/Umbraco.Core`; 130 in `src/Umbraco.Infrastructure`.
- **Impact**: Thread-pool starvation risk under load; each sync-over-async burns a pool thread for the duration of the awaited operation. Cold-start and burst traffic amplification. Many are in legacy interfaces that cannot be made async without a binary break.
- **Severity**: **Medium**.
- **Recommendation**: Inventory the 405 sites (one-off script) and classify: (a) pure-CPU `Task.FromResult` pattern — safe; (b) inside obsolete sync interface methods — unavoidable until next major; (c) avoidable — fix now. Publish the list and target (c) for v18. For (b), ensure the `async` overload is already present.

#### P-2. HybridCache migration is a real win but invalidation correctness is concentrated
- **Evidence**: `src/Umbraco.PublishedCache.HybridCache/CacheManager.cs` composes Content/Media/Members/Domains; 45 cache refreshers in Core; invalidation runs through notification handlers + `DatabaseServerMessenger`.
- **Impact**: Correctness of the published-content cache depends on every mutation path raising the right notification. A missing notification = stale frontend indefinitely. Surface area is large.
- **Severity**: **Medium** (strength in design, risk in coverage).
- **Recommendation**: Integration test harness that mutates content via each service API and asserts a corresponding `ContentCacheRefresherNotification` (or equivalent) is published. Cheap to write, catches a whole class of drift.

#### P-3. Known memory-management issues identified but not all fixed
- **Evidence**: `research-memory-leaks.md` lists 7 issues; lead one is undisposed `CancellationTokenSource` in `DatabaseServerMessenger.Dispose`.
- **Impact**: Practical impact rated negligible in the doc, but lead finding is a one-line fix.
- **Severity**: **Low**.
- **Recommendation**: Ship the one-line `Dispose` fix and the two `JsonDocument` correctness fixes called out in the doc. No reason to carry these forward.

### 5. Operations & Production Readiness

#### O-1. No production Dockerfile at repository root
- **Evidence**: `Dockerfile` exists only under `.devcontainer/`, `templates/UmbracoProject/`, `templates/UmbracoDockerCompose/Database/`. No production image build definition.
- **Impact**: Consumers must author their own image; "official" hosting guidance depends on external docs. Increases support burden.
- **Severity**: **Medium**.
- **Recommendation**: Publish a minimal production Dockerfile + compose example in `build/docker/` documenting the LB-critical envs (`__Host-` cookie domain, DataProtection key ring path, connection string). Reference it from README and hosting docs.

#### O-2. Load-balancing requirements are correct but scattered
- **Evidence**: `CLAUDE.md` mentions Data Protection key ring and NTP sync as prerequisites. `DatabaseServerRegistrarSettings`, `DatabaseServerMessengerSettings`, `DistributedJobSettings` each contain pieces of the picture.
- **Impact**: First-time LB deployments miss a setting and hit edge cases (token revocation across nodes, lock timeouts, etc.).
- **Severity**: **Medium**.
- **Recommendation**: Consolidate into a single "Production Load Balancing" ADR/doc listing: Data Protection, cookie settings, NTP, registrar, messenger poll interval, distributed-job timeout, server-role overrides, and the Subscriber read-only-DB pattern (already referenced in recent commits).

#### O-3. Test infrastructure and CI are strong
- **Evidence**: 535 unit test files, 631 integration test files, dedicated benchmarks project, acceptance tests with a live UmbracoProject instance, GitHub workflows + Azure Pipelines (`build/azure-pipelines.yml`). SQLite default + LocalDB opt-in for SQL Server-specific tests.
- **Impact**: Changes are caught in CI; SQL-server-specific concerns aren't masked.
- **Severity**: **Strength**.
- **Recommendation**: Preserve. Add a `umbracoLock` contention regression test that runs under LocalDB (not SQLite) exercising page-level lock behavior — protects against future regressions in the Finding D-1 fix.

#### O-4. AI-assisted review workflow is wired up well
- **Evidence**: `.github/workflows/claude-review.yml`, `claude.yml`; `CLAUDE.md` §7. Advisory, labels PRs, scoped bash allow-list.
- **Impact**: Low-cost quality safety net.
- **Severity**: **Strength**.
- **Recommendation**: None.

---

## Twelve-Factor Assessment

| Factor | Status | Note |
|---|---|---|
| **I. Codebase** | Met | Single repo, multiple artifacts; branching convention documented in `CLAUDE.md` §3. |
| **II. Dependencies** | Met | Centralized via `Directory.Packages.props`; transitive pinning enabled. |
| **III. Config** | Met | `IOptions<T>` throughout `Umbraco.Core/Configuration/Models/`; env-overridable via standard ASP.NET binding. |
| **IV. Backing Services** | Partial | DB is treated as an attachable resource, but it is *also* the message bus for cache invalidation (see D-2). That coupling is deliberate but violates the spirit of "treat backing services as attached resources". |
| **V. Build, Release, Run** | Met | `dotnet build`/`pack`/`test`; NerdBank.GitVersioning for build stamping. |
| **VI. Processes** | Partial | Stateless web tier, but reference-token storage and DataProtection keys must be shared out-of-band for scale-out (see O-2). |
| **VII. Port Binding** | Met | Standard ASP.NET Core Kestrel hosting. |
| **VIII. Concurrency** | Partial | Scale-out is supported, but constrained by `umbracoLock` page-level contention (D-1) and messenger poll cadence (D-2). |
| **IX. Disposability** | Partial | Most services honor `IDisposable`; `DatabaseServerMessenger.Dispose` bug is open (P-3). |
| **X. Dev/Prod Parity** | Partial | SQLite default in dev/test vs. SQL Server in prod means SQL-Server-specific issues (lock-table contention) can only be reproduced under LocalDB config (O-3 covers this). |
| **XI. Logs** | Met | Serilog everywhere with async sink. |
| **XII. Admin Processes** | Met | Migrations run via `MigrationPlan`; management operations are regular services invokable via API. |

---

## Key Architectural Decisions to Document

### Decision 1 — Binary compatibility via `[Obsolete]` + `StaticServiceProvider` + default interface methods
- **Context**: External consumers depend on v1.x interfaces; Umbraco commits to no binary breaks within a major.
- **Trade-offs accepted**: DI graph opacity (R-1), refactoring friction, permanent growth of Obsolete surface (R-2).
- **Reversal cost**: **High** — the pattern is idiomatic across ~700 sites; undoing it means a controlled breaking-changes release.
- **Recommendation**: **Accept**, with the instrumentation from R-1 (analyzer on new `StaticServiceProvider` uses) and the removal-gate from R-2.

### Decision 2 — Database as the distributed-system message bus
- **Context**: No external broker dependency; every install already has the DB.
- **Trade-offs accepted**: Cache-fan-out latency (D-2), lock-table contention (D-1).
- **Reversal cost**: **Medium** — `IServerMessenger` is already abstracted; an alternate implementation is pluggable.
- **Recommendation**: **Revisit** for multi-node deployments. Offer an optional push-based transport.

### Decision 3 — Dual ORMs (NPoco + EF Core) coexist indefinitely
- **Context**: Historical NPoco use + desire to modernize; migrating 251 services' repositories at once is infeasible.
- **Trade-offs accepted**: Two transaction systems, two migration stories, dual-implementation effort for new repos.
- **Reversal cost**: **High** — converging to one ORM is a multi-major program.
- **Recommendation**: **Revisit** — write an ADR making the target state explicit (R-3).

### Decision 4 — OpenAPI spec as a committed, manually refreshed artifact
- **Context**: Keeps spec versioned with the code; enables downstream consumers (UI client) to pin.
- **Trade-offs accepted**: Drift risk, noisy PR diffs, manual step.
- **Reversal cost**: **Low** — mechanized in a single CI job.
- **Recommendation**: **Reverse** (D-4) — generate at build time, fail on drift.

### Decision 5 — HybridCache as the unified published-content cache
- **Context**: Earlier cache implementations had known correctness/scale limitations; `Microsoft.Extensions.Caching.Hybrid` gives L1+L2 out of the box.
- **Trade-offs accepted**: Correctness depends on comprehensive invalidation coverage (P-2).
- **Reversal cost**: **Medium** — abstraction is clean; reverse is doable but unnecessary.
- **Recommendation**: **Accept**. Fund the invalidation-coverage test harness (P-2).

---

## Prioritized Roadmap

### Now (blocking issues or active risks)
- **Fix `DatabaseServerMessenger.Dispose` and `JsonDocument` disposal bugs** (from `research-memory-leaks.md`). One-line fix, already researched. → Removes a known resource-management defect before v17.5 GA.
- **Harden `umbracoLock` acquisition against page-level contention** (D-1). Apply `ROWLOCK` hint and/or row-padding; raise distributed-job write-lock default. → Unblocks customers hitting issue #22113.
- **Generate `OpenApi.json` at build time and diff-gate it** (D-4). → Eliminates silent API-surface drift.

### Next (force multipliers)
- **Publish ORM cutover ADR** (R-3). → Lets every new-repo PR answer "NPoco or EF Core?" by rule rather than taste.
- **Add cache-invalidation integration test harness** (P-2). → Catches any notification-coverage gap at CI, not in production.
- **Add analyzer flagging new `StaticServiceProvider` uses outside `[Obsolete]` delegation** (R-1). → Stops the shadow-DI pattern from growing organically.
- **Consolidate load-balancing ops documentation** (O-2) and add a production Dockerfile (O-1). → Reduces first-deploy surprises.

### Later (strategic investments)
- **Slice `IContentService` and peer mega-interfaces into role-scoped interfaces** (A-1). → Gives v18/v19 deprecation PRs a clean seam to work against.
- **Offer an optional push-based `IServerMessenger` transport** (D-2). → Removes DB polling latency as the LB fan-out ceiling.
- **Plan + stage the NPoco → EF Core cutover** per the ADR (R-3). → Converges the persistence story over v18–v20.
- **Formal pre-major deprecation-cleanup gate** (R-2). → Prevents the obsolete backlog from becoming permanent.

---
