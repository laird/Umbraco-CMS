# Critical Design Review — UriUtility Split (Review 1)

- **Plan reviewed**: `docs/superpowers/specs/2026-04-24-uriutility-split-design.md`
- **Date**: 2026-04-24
- **Prior reviews**: none

---

## 1. Overall Assessment

The plan correctly identifies a real bug (static fields on an instance class) and chooses a sensible split (app-path state vs. URI mapping). The back-compat strategy is faithful to the repo's documented `[Obsolete]` + `StaticServiceProvider` pattern. The spec is right-sized for a demo refactor. However, two design choices weaken the "clean immutable resolver" story it sells, and the success criteria don't sufficiently constrain integration-level regression. These are addressable without redesigning anything; they just need explicit decisions before the plan is written.

## 2. Critical Issues

### 2.1 The "obsolete test seam" reintroduces mutable state on the very class meant to be immutable
The spec states `Set/ResetAppDomainAppVirtualPath` will continue to work via "an `internal` seam on `ApplicationPathResolver` that permits re-seating the path." That gives the new resolver a mutable instance setter, which is the same shape (in miniature) as the bug being removed. It will live for 1–2 majors before removal.

**Why it matters**: The narrative of the refactor is "construct once, immutable resolver." A live mutable setter, even `internal`, contradicts that and makes thread-safety reasoning weaker (a singleton resolver could in principle be re-seated mid-request). It also bakes a back-channel into a brand-new class that future contributors will discover and use.

**Suggestion**: Keep `ApplicationPathResolver` strictly immutable. Implement the obsolete `Set/Reset` methods on the **facade** by replacing the facade's reference to the resolver with a freshly constructed `ApplicationPathResolver`. The facade's reference is `private`, so a single `volatile` field plus a swap is sufficient. The resolver class never gets a mutator. Bonus: the obsolete code path is concentrated in the facade, where it belongs.

### 2.2 Nullability divergence between facade (`string?`) and interface (`string`) is a real contract change, not "API sugar"
Section 3.2 keeps `UriUtility.AppPath`/`AppPathPrefix` as `string?` for binary compat, but the new `IApplicationPathResolver` exposes them as non-null `string`. Section 3.2's note calls this "purely an API-sugar win". It isn't — it's a stricter contract on the same underlying data. Consumers that currently null-check `UriUtility.AppPath` and switch to the new interface will get nullable-enable warnings telling them their checks are dead code. If the underlying value can be empty/null in any code path (e.g., before `IHostingEnvironment` is fully populated, or in test fakes), the new contract becomes a runtime null-deref waiting to happen.

**Why it matters**: This is exactly the kind of silent semantic drift the repo's centralized nullability rules (`<WarningsAsErrors>nullable</WarningsAsErrors>`) are meant to surface. Picking the wrong nullability now creates a third compatibility class to manage at v19 removal.

**Suggestion**: Decide explicitly. Two clean options: (a) keep `IApplicationPathResolver.AppPath`/`AppPathPrefix` as `string?` — matches existing data contract; lets future code tighten when consumers are audited; or (b) prove non-null at construction (constructor throws on null `ApplicationVirtualPath`), document the invariant, and write the contract as non-null. Don't ship a non-null interface contract on top of code that today returns nullable values from the same fields.

### 2.3 Success criteria don't cover integration tests
Section 9 lists unit-test passage and a build check, but the repo has 631 integration test files (per the architecture review). `UriUtility` is consumed by `UmbracoContext`, `DefaultUrlProvider`, `AliasUrlProvider`, `NewDefaultUrlProvider`, `PublishedUrlInfoProvider`, `UrlProviderExtensions`, `UmbracoVirtualPageRoute`, `UmbracoVirtualPageFilterAttribute`, `DefaultUrlAssembler`, `AddUnroutableContentWarningsWhenPublishingNotificationHandler` — i.e., the URL-resolution surface of the entire site. A unit-test-only success bar would not catch a regression where, say, the obsolete-ctor path resolves a different `IHostingEnvironment` than DI does.

**Why it matters**: This is a refactor of a code path that runs on every request. The blast radius of a subtle bug is high; the cost of running the integration suite is finite.

**Suggestion**: Add to Section 9: "Integration test suite under `tests/Umbraco.Tests.Integration` passes locally (SQLite default) before the PR is opened." Also add an explicit assertion that DI resolution of `UriUtility` via the **new** constructor and via the **obsolete** constructor produce singletons that observe the same `AppPath` for the same `IHostingEnvironment` instance.

### 2.4 The facade owns two references to the path resolver — one direct, one indirect through the mapper
Section 3.4's call graph shows the facade injecting `IApplicationPathResolver` directly *and* the mapper, where the mapper itself depends on `IApplicationPathResolver`. Because both are singletons, this is currently safe. But it embeds an invariant ("these two references resolve to the same instance") that holds only by coincidence of singleton registration. If someone in the future demotes either binding to `Scoped` (e.g., per-tenant path), the two views can diverge silently.

**Why it matters**: Hidden cross-component invariants enforced only by DI lifetime are the kind of thing that doesn't fail in tests and breaks in a multi-tenant or testing context months later.

**Suggestion**: Either (a) document this invariant explicitly in the design, with a registration-time test that both interfaces resolve to the same path-resolver instance; or (b) have `UriUtility` inject only the mapper and expose the path-resolver through it (e.g., `IUmbracoUriMapper.PathResolver`). (b) is structurally cleaner — the mapper already needs the resolver, and the facade only needs to surface what the mapper exposes plus what callers historically asked of the resolver.

## 3. Previously Addressed Items

None — this is the first review.

## 4. Alternative Architectural Challenge

**Alternative**: *Fix the bug, don't extract the interfaces (yet).*

Convert `UriUtility`'s static fields to instance fields. Delete the static-state setters and replace them with constructor injection of `IHostingEnvironment` (already present). Stop. Do not introduce `IApplicationPathResolver` or `IUmbracoUriMapper`. Add the interfaces only when a concrete consumer asks for the narrower seam.

**Pros**:
- Zero new public surface; no `[Obsolete]` debt.
- No `StaticServiceProvider` use at all (the current spec adds one site).
- Smallest possible diff — easiest to land, review, and revert.
- Aligns with YAGNI: the architecture review identified `StaticServiceProvider` growth and obsolete backlog as risks (Findings R-1, R-2). Adding none of either is the strictly safer move.
- The bug fix is independently valuable; the interface split can come later under its own justification.

**Cons**:
- No narrower interfaces for new code to inject — defers the testability win.
- Doesn't satisfy the user's stated preference for option C ("extract URI parsing vs. app-path logic into separate classes").
- Leaves `UriUtility` doing two things; a future split has to pay the same back-compat cost it would today.

This isn't a strict either/or — the alternative is "do less now, more later." It's worth weighing because the spec's primary justification for the split is architectural cleanliness, and the cleanliness is partially diluted by issues 2.1, 2.2, and 2.4 above.

## 5. Minor Issues & Improvements

- **Thread safety is not stated.** `ApplicationPathResolver`, if truly immutable after construction, is trivially thread-safe; `UmbracoUriMapper` is stateless. Spec should say so in one line — singletons read in request paths must justify their thread-safety.
- **Behavior on null `ApplicationVirtualPath` is implicit.** Current `SetAppDomainAppVirtualPath` does `appPath ?? "/"`. The new constructor must either (a) preserve that fallback or (b) throw — and the spec should say which. This intersects with issue 2.2.
- **Section 4.2 mentions "disposing"** an `ApplicationPathResolver`, but the class has no reason to be `IDisposable`. Drop the disposal language; the test is just "two instances, two app paths, observe independence."
- **Section 4.3 ("Coverage target") is aspirational, not enforceable.** Either drop it or convert to a concrete rule (e.g., "every assertion in `UriUtilityTests` that exercises `ToAbsolute`/`ToAppRelative`/`ResolveUrl` must have a corresponding assertion in `ApplicationPathResolverTests`").
- **Success criterion 5** (`grep -n "static" UriUtility.cs` returns no field declarations) is a useful smoke test but greps too narrowly — `static readonly` constants would be fine, and `static` keywords on the obsolete `Set/Reset` methods aren't reachable here. Tighten the grep to field declarations specifically, or just check that the file size shrank materially.

## 6. Questions for Clarification

1. **Does any production code path call the old `Set/ResetAppDomainAppVirtualPath` outside tests?** If yes, the obsolete forwarding path must keep working transparently; if no, the obsolete methods can be made `[EditorBrowsable(Never)]` and stop being a documented part of the API immediately.
2. **Is the singleton lifetime load-bearing for `IApplicationPathResolver`, or could it be `Transient`?** Answer affects issue 2.4. If transient is acceptable, the cross-component invariant in 2.4 disappears.
3. **What happens if a test today instantiates `UriUtility` directly (not via DI)?** The new obsolete ctor calls `StaticServiceProvider.Instance.GetRequiredService<IApplicationPathResolver>()`. If the test environment has no DI container set up, this throws. Need to either (a) audit existing direct-instantiation sites, or (b) keep the old ctor's behavior of constructing the resolver from `IHostingEnvironment` directly without going through DI.

## 7. Final Recommendation

**Approve with changes.**

Required changes before writing the implementation plan:

1. Resolve issue 2.1 — keep `ApplicationPathResolver` strictly immutable; obsolete-state mutation lives on the facade only.
2. Resolve issue 2.2 — explicit decision on nullability of `IApplicationPathResolver.AppPath`/`AppPathPrefix`, with a one-line justification in the spec.
3. Resolve issue 2.3 — extend Section 9 success criteria to include integration tests and a DI-equivalence assertion.
4. Resolve issue 2.4 — either document the singleton-equivalence invariant with a test, or restructure the facade to inject only the mapper.
5. Answer Question 6.3 to confirm the obsolete ctor's `StaticServiceProvider` path will work everywhere `UriUtility` is constructed today.

Issues 5 (minor) can be folded in opportunistically.

---
