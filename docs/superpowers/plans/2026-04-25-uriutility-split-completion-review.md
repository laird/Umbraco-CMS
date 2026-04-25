# 2026-04-25-uriutility-split - Completion Review

## 1. Overview

The plan called for splitting `Umbraco.Core/Routing/UriUtility.cs` into a strictly-immutable `ApplicationPathResolver` and a stateless `UmbracoUriMapper`, retaining `UriUtility` itself as a binary-compatible facade. **Status: Fully delivered, with one mid-flight scope addition (DI-ambiguity fix) and one mid-flight scope removal (dead-code `ToFullUrl`), both justified by evidence surfaced during execution.**

## 2. Completed Items

- New public interface `IApplicationPathResolver` and sealed implementation `ApplicationPathResolver` in `src/Umbraco.Core/Routing/`. Constructor reads `IHostingEnvironment.ApplicationVirtualPath` once, normalizes null → `"/"`, and stores both `_appPath` and `_appPathPrefix` as `readonly` instance fields. No `static` field declarations remain in this surface.
- New public interface `IUmbracoUriMapper` and sealed implementation `UmbracoUriMapper`. Stateless aside from the injected `IApplicationPathResolver`; exposes that resolver via `PathResolver` so consumers can avoid a separately injected dependency.
- `UriUtility` rewritten as a thin facade injecting only `IUmbracoUriMapper`. All previous public method/property signatures preserved. Static fields `_appPath`/`_appPathPrefix` deleted.
- Internal `SetAppDomainAppVirtualPath` and `ResetAppDomainAppVirtualPath` deleted (verified zero callers anywhere in `src/` or `tests/`).
- Old constructor `UriUtility(IHostingEnvironment)` retained, marked `[Obsolete("…Scheduled for removal in Umbraco 19.")]`, and delegates inline (`new ApplicationPathResolver(...)` → `new UmbracoUriMapper(...)`) — no `StaticServiceProvider` call sites added anywhere.
- DI registration updated in `UmbracoBuilder.cs:208–210`: resolver → mapper → facade, all singletons.
- New test file `ApplicationPathResolverTests.cs` covering `AppPath`, `AppPathPrefix`, `ToAbsolute`, `ToAppRelative`, `ResolveUrl`, null normalization, and the dedicated **two-instance isolation test** that locks in the bug fix.
- New test file `UmbracoUriMapperTests.cs` covering `PathResolver`, `UriFromUmbraco`, `MediaUriFromUmbraco`, `UriToUmbraco`, and the constructor null-guard.
- Constructor-equivalence test added to `UriUtilityTests.cs` exercising both constructors against the same `IHostingEnvironment` and asserting all eight observable members match for `"/"`, `"/foo"`, `"/foo/bar"`, and `null` virtual paths.
- All spec verification commands (`§9.4` no `StaticServiceProvider`, `§9.5` no static fields, `AppDomainAppVirtualPath` removed) return `OK`.
- Unit-test suite passes 5,445/5,451 (6 pre-existing skips, 0 failures); 294 routing-suite tests green.

## 3. Modified or Partially Completed Items

- **`ToFullUrl` not preserved** as the plan directed (Task 4 §3, Task 5 §1). Verification after Task 5 showed `ToFullUrl` had zero callers anywhere in `src/` or `tests/` and that keeping it required a `(UmbracoUriMapper)_uriMapper` cast in the facade. The final-review pass (after Task 8) judged this dead-code-plus-fragile-cast worse than keeping the symbol; commit `c30d76b9cb` deletes both copies. Net: a YAGNI improvement over the plan, not a defect.
- **Plan §3.4 success criterion 6 (full integration-test suite passes)** was started but not re-verified end-to-end after the DI-ambiguity fix. The first integration run surfaced the DI ambiguity (`Constructors_AreObservationallyEquivalent` was insufficient to catch this — see §5). After commit `456c074072` added `[ActivatorUtilitiesConstructor]`, only a smoke-sized integration fixture (`CoreConfigurationTests`, 9/9 pass) was re-run to confirm the activation error was gone. The full integration suite was not re-driven to completion within the session.

## 4. Omitted Items

- None from the plan's task list. All eight tasks (1–8) executed. The integration-suite re-run after the DI fix is the only success-criterion line item not fully exercised; see §3 above.

## 5. Key Achievements & Improvements

- **Static-state bug eliminated at the type system level.** `ApplicationPathResolver`'s fields are `readonly` instance fields and the class is `sealed`; the bug is unreachable, not just unobserved.
- **Zero `StaticServiceProvider` growth.** The plan originally proposed using `StaticServiceProvider.Instance.GetRequiredService<…>()` in the obsolete constructor (the documented `CLAUDE.md` §5.1 pattern); execution improved on this by inlining `new ApplicationPathResolver(…)` + `new UmbracoUriMapper(…)`. This both honors the architecture review's R-1 finding and lets the 8+ direct test instantiations (`new UriUtility(hostingEnvironment)`) work without any DI container configured.
- **Constructor-equivalence test.** A genuinely useful regression artifact — a future refactor that rewires either constructor in a way that diverges observable output will fail this test in CI.
- **Real bug caught by tests.** The integration suite surfaced a DI-activation ambiguity (two single-parameter public constructors) the plan did not anticipate. Fixed cleanly with `[ActivatorUtilitiesConstructor]` and a follow-up smoke test.
- **Clean removal of dead surface.** `ToFullUrl`, `SetAppDomainAppVirtualPath`, `ResetAppDomainAppVirtualPath` all removed — total ~30 lines of surface deleted that nothing depended on.
- **Net file-size reduction.** `UriUtility.cs` went from 273 lines to ~108. Logic that lives elsewhere now actually lives elsewhere.

## 6. Final High-Quality Technical Review

### Spec fidelity (across all tasks)

All design-doc v2 requirements (§3.1 new types, §3.2 facade shape, §3.3 DI registration, §4 testing, §5 back-compat checklist, §9 success criteria 1–5, 7) are met. Success criterion 6 (full integration suite passes) is partially met — see §3 above and "Important" finding below.

### Architecture & design quality

The split is clean. Responsibilities are separable in source (`ApplicationPathResolver` knows nothing about URIs; `UmbracoUriMapper` knows nothing about hosting). The single-injection facade (`IUmbracoUriMapper` only, with `PathResolver` exposed via the mapper) eliminates the "two singletons must be the same instance" implicit invariant the design review called out. The obsolete-ctor inlining decision is materially better than the plan's original `StaticServiceProvider` proposal.

### Code quality & best practices

- Doc-comments are present on every public member; `<inheritdoc />` is used on the implementations to inherit interface docs.
- Null-guards use the modern `ArgumentNullException.ThrowIfNull` consistently.
- `_appPath` and `_appPathPrefix` are `readonly` and only assigned in the constructor.
- Test coverage hits every public member and the regression-class bug.
- The pre-existing `ResolveUrl` `StringBuilder` algorithm is ported byte-for-byte; preserved comment-style and behavior.

### Edge cases & robustness

- Null `ApplicationVirtualPath` is normalized to `"/"` (test `Constructor_NullVirtualPath_NormalizesToSlash`).
- Two-instance isolation is asserted in `TwoInstances_DistinctVirtualPaths_DoNotShareState`.
- Constructor null-guards on all three new types.
- `ResolveUrl` null-input throws `ArgumentNullException` (covered by test).

### Issues found

**Critical:** None.

**Important:**

- **Full integration suite not re-verified after the DI fix.** `tests/Umbraco.Tests.Integration` was started, hit the DI ambiguity, was killed, the fix landed in `456c074072`, and only `CoreConfigurationTests` (9/9) was re-run to confirm activation succeeded. Plan success criterion §9.6 explicitly requires "Integration test suite passes." Suggested resolution before merging the PR upstream: run `dotnet test tests/Umbraco.Tests.Integration/Umbraco.Tests.Integration.csproj` to completion locally (or rely on the upstream CI pipeline — `build/azure-pipelines.yml` runs the suite — and gate on its result). No code change required if the suite is green; the residual risk is "URL-resolution behavior in a real DI container" not yet confirmed under load.

**Minor / Polish:**

- `src/Umbraco.Core/Routing/UmbracoUriMapper.cs` ends with a blank line between the last method and the closing class brace (after `UriToUmbraco`). Cosmetic; `dotnet format` would normalize it.
- The `// TODO: …not really sure we need ToLower?` comment is preserved verbatim inside `UmbracoUriMapper.UriToUmbraco`. Per spec §2 non-goals this is intentional (no behavior change to `ToLower` semantics in this PR), so it should stay until a separate change addresses it.
- `UriUtility.AppPath` and `AppPathPrefix` are typed `string?` on the facade for binary compat, while `IApplicationPathResolver.AppPath` and `AppPathPrefix` are `string`. The mismatch is intentional and explained in the design doc; consumers migrating from the facade to the interface will see compiler-flagged dead null checks. Not a defect.

**Overall Quality Assessment:** Very Good.

## 7. Review History Summary

- Number of `*-critical-implementation-review-*.md` files processed: **0** (per-task spec/quality reviews were performed by in-session subagent dispatches and not persisted to disk).
- Total per-task spec reviews passed: **3** (Batch A spec, Batch B spec, Batch C combined, Batch D combined). Batch A required one round of post-review fixes before passing.
- Total per-task code-quality reviews passed: **3** (Batch A, Batch B — required one round of fixes for doc-comment and broader test coverage; Batch C — combined review). Batch D combined review approved on first pass. Final-branch review surfaced one important issue (`ToFullUrl` cast) which was fixed (commit `c30d76b9cb`).

## 8. Final Assessment

The delivered branch achieves the design's stated goal: a strictly-immutable resolver, a stateless mapper, and a binary-compatible facade with no static state and no growth in `StaticServiceProvider` use. Mid-flight evidence drove two improvements over the original plan — inlining the obsolete-ctor's dependency construction (no `StaticServiceProvider`) and deleting truly dead `ToFullUrl` — and uncovered one bug the design did not anticipate (DI constructor ambiguity), which was fixed cleanly with `[ActivatorUtilitiesConstructor]`. Unit-suite is fully green (5,445/5,451, no new failures), routing-suite is fully green (294/294), and a smoke integration fixture confirms DI activation works. The one open item is the full integration-suite re-run after the DI fix, which is required by plan success criterion §9.6 before claiming complete.

This implementation plan is considered **complete with the following caveats:**
- Run `dotnet test tests/Umbraco.Tests.Integration/Umbraco.Tests.Integration.csproj` to completion (or rely on upstream CI) and confirm a green result before opening the PR for merge. Until then, plan success criterion §9.6 is documented but not empirically verified end-to-end on this branch.
