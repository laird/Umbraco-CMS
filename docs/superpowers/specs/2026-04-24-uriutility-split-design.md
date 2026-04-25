# Design: Split `UriUtility` into `ApplicationPathResolver` + `UmbracoUriMapper`

- **Date**: 2026-04-24
- **Branch**: `planfirst`
- **Version**: 2 (incorporates critical-design-review 1)
- **Scope**: Single-file refactor with back-compat facade. Demo-sized.
- **Status**: Design revised post-review; awaiting user approval before planning.

---

## 1. Problem

`src/Umbraco.Core/Routing/UriUtility.cs` is a `public sealed class` registered as a DI singleton, but stores its state in **`private static` fields** (`_appPath`, `_appPathPrefix` at lines 13–14). Instance properties (`AppPath`, `AppPathPrefix`) and instance methods (`ToAbsolute`, `ToAppRelative`, `ResolveUrl`) read that static state directly.

Consequences:

- Two instances silently share state. Constructing a second `UriUtility` with a different `IHostingEnvironment.ApplicationVirtualPath` mutates the first.
- The internal `SetAppDomainAppVirtualPath(string)` and `ResetAppDomainAppVirtualPath(IHostingEnvironment)` methods mutate ambient state, but no caller invokes them outside the constructor itself — verified via `grep` across `src/` and `tests/`.
- The class mixes two distinct responsibilities: owning the app-path state and mapping Umbraco-internal URIs to/from public URIs.

## 2. Goals / Non-goals

**Goals**
- Eliminate the static-mutable-state bug.
- Split `UriUtility` along its natural seam: app-path logic vs. URI mapping.
- Preserve binary compatibility for all existing consumers (15 internal call sites; 8+ direct `new UriUtility(hostingEnvironment)` test sites; unknown external packages).
- Provide narrower interfaces that new code can inject.
- Add zero new `StaticServiceProvider` call sites.

**Non-goals**
- No consumer rewrites.
- No change to `UriUtilityCore` (a separate static helper).
- No fix for the `ToLower` TODO at line 134–135.
- No behavioral change to `ResolveUrl` — its string-building logic is preserved byte-for-byte.

## 3. Design

### 3.1 New types

All in `src/Umbraco.Core/Routing/`.

**`IApplicationPathResolver` / `ApplicationPathResolver`**
- Owns app-path state as **instance** fields (the bug fix). Strictly immutable after construction.
- Depends on `IHostingEnvironment`; reads `ApplicationVirtualPath` once in the constructor and normalizes a null value to `"/"`, preserving existing defensive behavior.
- Public surface (all return non-null `string`):
  - `string AppPath { get; }` — `"/"` or `"/foo"`.
  - `string AppPathPrefix { get; }` — `""` or `"/foo"`.
  - `string ToAbsolute(string url)`
  - `string ToAppRelative(string virtualPath)`
  - `string ResolveUrl(string relativeUrl)`

**`IUmbracoUriMapper` / `UmbracoUriMapper`**
- Stateless URI transforms; depends on `IApplicationPathResolver`.
- Exposes the resolver it already depends on, eliminating any possibility of two consumers seeing different resolver instances:
  - `IApplicationPathResolver PathResolver { get; }`
  - `Uri UriFromUmbraco(Uri uri, RequestHandlerSettings requestConfig)`
  - `Uri MediaUriFromUmbraco(Uri uri)`
  - `Uri UriToUmbraco(Uri uri)`
  - `Uri ToFullUrl(string absolutePath, Uri currentRequestUrl)` — was `internal` on `UriUtility`; remains `internal` on `UmbracoUriMapper` (not promoted to the interface).

**Thread-safety statement**: `ApplicationPathResolver` is immutable after construction; `UmbracoUriMapper` is stateless aside from its injected resolver. Both are safely usable as singletons across threads. The same applies to the `UriUtility` facade described below.

### 3.2 `UriUtility` becomes a facade

`UriUtility` stays `public sealed` in its existing namespace/file. Purpose: preserve binary compatibility for existing DI consumers and direct test instantiations, who often want both capabilities together.

- **Single dependency**: the facade injects **only `IUmbracoUriMapper`**. App-path operations are reached through `IUmbracoUriMapper.PathResolver`. This guarantees, by construction, that the facade and the mapper never observe a different `IApplicationPathResolver` instance — no implicit DI-lifetime invariant.
- **New constructor**: `public UriUtility(IUmbracoUriMapper uriMapper)` — stores it (`readonly`) and delegates every method/property to it.
- **Old constructor**: `[Obsolete("Use the constructor with IUmbracoUriMapper. Scheduled for removal in Umbraco 19.")] public UriUtility(IHostingEnvironment hostingEnvironment)` — calls the new constructor via `: this(...)` by **constructing its dependencies inline**, *not* via `StaticServiceProvider`:
  ```csharp
  [Obsolete("...")]
  public UriUtility(IHostingEnvironment hostingEnvironment)
      : this(BuildMapper(hostingEnvironment))
  {
  }

  private static IUmbracoUriMapper BuildMapper(IHostingEnvironment hostingEnvironment)
  {
      ArgumentNullException.ThrowIfNull(hostingEnvironment);
      var resolver = new ApplicationPathResolver(hostingEnvironment);
      return new UmbracoUriMapper(resolver);
  }
  ```
  This eliminates the only `StaticServiceProvider` site the original design proposed, makes the obsolete ctor work in tests with no DI container configured, and keeps obsolete behavior identical to today's: each direct `new UriUtility(hostingEnvironment)` produces a self-contained pair of fresh resolver + mapper.
- **Static fields `_appPath` and `_appPathPrefix` are deleted.**
- **`SetAppDomainAppVirtualPath` and `ResetAppDomainAppVirtualPath` are deleted outright** — verified via `grep` to have no callers anywhere in `src/` or `tests/`. No `[Obsolete]` deprecation period needed because no consumer depends on them. (The deprecation policy in `CLAUDE.md` §5 applies to public/protected API; these were `internal` and unreachable.)
- **Instance properties and methods delegate** to the mapper:
  - `AppPath` → `_uriMapper.PathResolver.AppPath`
  - `AppPathPrefix` → `_uriMapper.PathResolver.AppPathPrefix`
  - `ToAbsolute` → `_uriMapper.PathResolver.ToAbsolute`
  - `ToAppRelative` → `_uriMapper.PathResolver.ToAppRelative`
  - `ResolveUrl` → `_uriMapper.PathResolver.ResolveUrl`
  - `UriFromUmbraco`, `MediaUriFromUmbraco`, `UriToUmbraco`, `ToFullUrl` → `_uriMapper.*`
- **Nullability**: `IApplicationPathResolver.AppPath` and `AppPathPrefix` are typed non-null `string` because the resolver's constructor normalizes a null `ApplicationVirtualPath` to `/`, matching existing defensive behavior. `UriUtility`'s same-named properties keep `string?` signatures for binary compatibility; the implicit non-null-to-nullable widening is benign under nullable-enable. Consumers that migrate from `UriUtility` to `IApplicationPathResolver` get a stricter, accurate contract; their existing null checks become dead code that the compiler will flag — the design accepts this and treats it as an improvement, not a breaking change.

### 3.3 DI registration

In `src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs` (current registration at line 208 is `Services.AddSingleton<UriUtility>();`):

```csharp
Services.AddSingleton<IApplicationPathResolver, ApplicationPathResolver>();
Services.AddSingleton<IUmbracoUriMapper, UmbracoUriMapper>();
Services.AddSingleton<UriUtility>();
```

All singletons — consistent with current behavior. `UriUtility`'s new constructor resolves `IUmbracoUriMapper` from DI normally.

### 3.4 Call graph after the refactor

```
Legacy consumer (e.g., DefaultUrlProvider, UmbracoContext, ~15 sites)
    └── UriUtility (facade, singleton)
          └── IUmbracoUriMapper (singleton)
                └── IApplicationPathResolver (singleton, also exposed via .PathResolver)

Legacy test consumer (e.g., UriUtilityTests, TestHelperBase, ~8 sites)
    └── new UriUtility(IHostingEnvironment)   [obsolete ctor, no DI required]
          └── new UmbracoUriMapper(new ApplicationPathResolver(hostingEnvironment))

New consumers inject IApplicationPathResolver and/or IUmbracoUriMapper directly.
```

## 4. Testing

### 4.1 Regression guard (unchanged)

- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UriUtilityTests.cs`
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/CoreThings/UriUtilityCoreTests.cs`

Both must continue to pass unchanged — they exercise the facade surface and are the regression guard.

### 4.2 New tests

**`ApplicationPathResolverTests.cs`** (same directory as `UriUtilityTests.cs`)
- Covers `AppPath`, `AppPathPrefix`, `ToAbsolute`, `ToAppRelative`, `ResolveUrl` directly against the new instance class.
- **Instance-isolation test**: construct two `ApplicationPathResolver` instances with distinct `IHostingEnvironment.ApplicationVirtualPath` values (e.g., `"/"` and `"/foo"`); assert each reports its own `AppPath`/`AppPathPrefix` and that the values returned by `ToAbsolute`/`ToAppRelative` differ accordingly. This is the test that locks in the bug fix.
- **Null-normalization test**: constructor with an `IHostingEnvironment` whose `ApplicationVirtualPath` returns null produces a resolver where `AppPath == "/"` and `AppPathPrefix == ""`.

**`UmbracoUriMapperTests.cs`** (same directory)
- Covers `UriFromUmbraco`, `MediaUriFromUmbraco`, `UriToUmbraco`, `ToFullUrl` with a stub or mock `IApplicationPathResolver`.
- Verifies `PathResolver` exposes the same instance that was injected.

### 4.3 Equivalence test (added per review)

In `UriUtilityTests.cs` (the existing regression-guard suite), add a constructor-equivalence test:

- For a representative set of `IHostingEnvironment.ApplicationVirtualPath` values (`"/"`, `"/foo"`, `"/foo/bar"`, `null`), construct `UriUtility` two ways:
  1. Via the obsolete `(IHostingEnvironment)` constructor.
  2. Via the new `(IUmbracoUriMapper)` constructor with `new UmbracoUriMapper(new ApplicationPathResolver(hostingEnvironment))`.
- Assert both report identical `AppPath`, `AppPathPrefix`, and identical results for a representative input set against `ToAbsolute`, `ToAppRelative`, `ResolveUrl`, `UriFromUmbraco`, `UriToUmbraco`, `MediaUriFromUmbraco`, and `ToFullUrl`.

This locks down the "obsolete-ctor and new-ctor are observationally equivalent" invariant.

### 4.4 Coverage rule

Every assertion in `UriUtilityTests` that exercises `ToAbsolute`/`ToAppRelative`/`ResolveUrl` has a corresponding assertion in `ApplicationPathResolverTests`. Every assertion in `UriUtilityTests` that exercises `UriFromUmbraco`/`MediaUriFromUmbraco`/`UriToUmbraco`/`ToFullUrl` has a corresponding assertion in `UmbracoUriMapperTests`. So the narrower classes have standalone coverage even after the facade is retired in a future major.

## 5. Back-compat checklist

Matches `CLAUDE.md` §5 rules:

- [x] Old constructor marked `[Obsolete]` with "Scheduled for removal in Umbraco 19." (current major is 17, per `version.json`).
- [x] Old constructor delegates to new via `: this(...)`.
- [x] No `StaticServiceProvider` calls — old ctor builds its dependencies inline. This is more conservative than the documented pattern in `CLAUDE.md` §5.1 and is preferred here because direct test instantiation (`new UriUtility(hostingEnvironment)`) must work without a configured DI container.
- [x] DI registers the new constructor (via `IUmbracoUriMapper`); old constructor is external/test-only.
- [x] Internal `Set/ResetAppDomainAppVirtualPath` methods deleted (no callers exist; verified by grep).
- [x] No `CS0618` suppression sites needed (no internal code calls obsolete members).

## 6. Risks

- **Delegation drift**: facade methods that delegate to the wrong injected dependency (e.g., `UriFromUmbraco` reaching the resolver instead of the mapper) would compile and pass narrow unit tests but break URL generation in production. Mitigated by the constructor-equivalence test (§4.3) and the integration suite (success criterion 6).
- **Migrating consumers see dead-code warnings** on previously needed null checks for `AppPath`/`AppPathPrefix`. Intentional — the new contract is honest about runtime behavior.
- **External packages** that subclass or replace the static fields directly (none expected — fields were `private`) would break. The fields were not part of the API surface; this is non-binding.
- **Direct test instantiation** of `UriUtility(IHostingEnvironment)` must continue to work without DI. Mitigated by the obsolete ctor's inline dependency construction (§3.2). 8+ test sites verified via grep.

## 7. Open questions / closed alternatives

**Closed alternatives**:
- *Fix the bug, don't extract interfaces (review §4 alternative)*: rejected during brainstorming. The user explicitly chose the full extract (option C) to maximize demo payoff and create narrower seams for future consumers. The extract pays the same `[Obsolete]` cost it would today; doing it now is therefore neither more nor less expensive than deferring.
- *Inject `IApplicationPathResolver` directly into the facade alongside the mapper*: rejected to remove the implicit "both DI bindings resolve to the same instance" invariant (§3.2).
- *Keep `Set/ResetAppDomainAppVirtualPath` with a mutable `internal` seam on the new resolver*: rejected — no callers exist; the methods are dead code.

**Open questions at design time**: none.

## 8. Files touched

**New (Core)**
- `src/Umbraco.Core/Routing/IApplicationPathResolver.cs`
- `src/Umbraco.Core/Routing/ApplicationPathResolver.cs`
- `src/Umbraco.Core/Routing/IUmbracoUriMapper.cs`
- `src/Umbraco.Core/Routing/UmbracoUriMapper.cs`

**Modified**
- `src/Umbraco.Core/Routing/UriUtility.cs` — facade rewrite. Static fields and `Set/Reset` methods deleted; new ctor added; old ctor obsoleted and delegates inline.
- `src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs` — add two singleton registrations alongside existing `UriUtility`.

**New (tests)**
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/ApplicationPathResolverTests.cs`
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UmbracoUriMapperTests.cs`

**Modified (tests)**
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UriUtilityTests.cs` — add the constructor-equivalence test (§4.3). Existing assertions remain unchanged.

**Unchanged but verified**
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/CoreThings/UriUtilityCoreTests.cs` — must still pass.
- All 8+ direct `new UriUtility(hostingEnvironment)` sites in tests — must still pass without DI configuration.

## 9. Success criteria

1. All existing tests in `UriUtilityTests.cs` and `UriUtilityCoreTests.cs` pass unchanged (apart from the new equivalence test added to the former).
2. `ApplicationPathResolverTests.cs` contains an instance-isolation test that would have failed against the old static-field implementation.
3. Full solution builds with `<Nullable>enable</Nullable>` and `<WarningsAsErrors>nullable</WarningsAsErrors>` (as set in `Directory.Build.props`).
4. **No new `StaticServiceProvider` call sites.** Verified via `grep -rn "StaticServiceProvider" src/Umbraco.Core/Routing/UriUtility.cs` returning no matches.
5. `UriUtility.cs` declares no `static` fields. Verified via `grep -E '^\s*(private|public|internal|protected)\s+static\s+\w' src/Umbraco.Core/Routing/UriUtility.cs` returning no matches.
6. **Integration test suite passes**: `tests/Umbraco.Tests.Integration` (default SQLite configuration) passes locally before the PR is opened. Specifically, any test that exercises URL generation or routing must pass unchanged.
7. **Constructor equivalence**: the equivalence test in `UriUtilityTests.cs` (§4.3) passes for the documented input matrix.

---

## Changelog (v1 → v2)

- **CDR1-C1**: Eliminated the proposed mutable test seam on `ApplicationPathResolver`. Deleted `Set/ResetAppDomainAppVirtualPath` outright (no callers exist anywhere in `src/` or `tests/`, verified by grep). The new resolver is strictly immutable. Section 3.2, 6.
- **CDR1-C2**: Resolved nullability divergence. `IApplicationPathResolver.AppPath` and `AppPathPrefix` are now non-null `string`; constructor normalizes null `ApplicationVirtualPath` to `"/"`, matching existing defensive behavior. Facade keeps `string?` for binary compat via implicit widening. Sections 3.1, 3.2, 6.
- **CDR1-C3**: Added integration-test success criterion (§9.6) and constructor-equivalence test (§4.3, §9.7). New risk entry "Delegation drift" in §6.
- **CDR1-C4**: Restructured the facade to inject only `IUmbracoUriMapper`. The mapper interface gains `PathResolver { get; }`. This eliminates the "two DI bindings must resolve to the same instance" implicit invariant. Sections 3.1, 3.2, 3.4.
- **CDR1-Alt**: Reviewed the "fix the bug, don't extract" alternative. Stays rejected; rationale recorded in §7 closed alternatives.
- **CDR1-M1**: Added thread-safety statement to §3.1.
- **CDR1-M2**: Null `ApplicationVirtualPath` behavior made explicit in §3.1 (covered by C2 fix).
- **CDR1-M3**: Removed disposal language from §4.2; rephrased the instance-isolation test.
- **CDR1-M4**: Replaced aspirational "Coverage target" with concrete coverage rule in §4.4.
- **CDR1-M5**: Tightened success-criterion grep to match field declarations specifically (§9.5).
- **H1 (holistic)**: Eliminated the only `StaticServiceProvider` call site. Old ctor builds its dependencies inline (§3.2). Adds explicit goal "Add zero new `StaticServiceProvider` call sites" in §2.
- **H2 (holistic)**: Documented direct test-instantiation path (§3.4 call graph, §6 risks). Verified by grep that 8+ test sites do `new UriUtility(hostingEnvironment)` directly; the obsolete ctor must work without DI.
