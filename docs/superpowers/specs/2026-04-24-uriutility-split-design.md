# Design: Split `UriUtility` into `ApplicationPathResolver` + `UmbracoUriMapper`

- **Date**: 2026-04-24
- **Branch**: `planfirst`
- **Scope**: Single-file refactor with back-compat facade. Demo-sized.
- **Status**: Design approved; awaiting spec review before planning.

---

## 1. Problem

`src/Umbraco.Core/Routing/UriUtility.cs` is a `public sealed class` registered as a DI singleton, but stores its state in **`private static` fields** (`_appPath`, `_appPathPrefix` at lines 13–14). Instance properties (`AppPath`, `AppPathPrefix`) and instance methods (`ToAbsolute`, `ToAppRelative`, `ResolveUrl`) read that static state directly.

Consequences:

- Two instances silently share state. Constructing a second `UriUtility` with a different `IHostingEnvironment.ApplicationVirtualPath` mutates the first.
- The internal `SetAppDomainAppVirtualPath(string)` and `ResetAppDomainAppVirtualPath(IHostingEnvironment)` methods mutate ambient state — easy to forget to reset between tests.
- The class mixes two distinct responsibilities: owning the app-path state and mapping Umbraco-internal URIs to/from public URIs.

## 2. Goals / Non-goals

**Goals**
- Eliminate the static-mutable-state bug.
- Split `UriUtility` along its natural seam: app-path logic vs. URI mapping.
- Preserve binary compatibility for all existing consumers (15 internal call sites; unknown external packages).
- Provide narrower interfaces that new code can inject.

**Non-goals**
- No consumer rewrites.
- No change to `UriUtilityCore` (a separate static helper).
- No fix for the `ToLower` TODO at line 134–135.
- No behavioral change to `ResolveUrl` — its string-building logic is preserved byte-for-byte.

## 3. Design

### 3.1 New types

All in `src/Umbraco.Core/Routing/`.

**`IApplicationPathResolver` / `ApplicationPathResolver`**
- Owns app-path state as **instance** fields (the bug fix).
- Depends on `IHostingEnvironment`; reads `ApplicationVirtualPath` once in the constructor.
- Public surface:
  - `string AppPath { get; }` — always non-null; `"/"` or `"/foo"`.
  - `string AppPathPrefix { get; }` — always non-null; `""` or `"/foo"`.
  - `string ToAbsolute(string url)`
  - `string ToAppRelative(string virtualPath)`
  - `string ResolveUrl(string relativeUrl)`

**`IUmbracoUriMapper` / `UmbracoUriMapper`**
- Stateless URI transforms; depends on `IApplicationPathResolver`.
- Public surface:
  - `Uri UriFromUmbraco(Uri uri, RequestHandlerSettings requestConfig)`
  - `Uri MediaUriFromUmbraco(Uri uri)`
  - `Uri UriToUmbraco(Uri uri)`
  - `Uri ToFullUrl(string absolutePath, Uri currentRequestUrl)` — was `internal` on `UriUtility`; remains `internal` on `UmbracoUriMapper` (not promoted to the interface).

### 3.2 `UriUtility` becomes a facade

`UriUtility` stays `public sealed` in its existing namespace/file. Purpose: preserve binary compatibility for existing DI consumers, who often want both capabilities together.

- **New constructor**: `public UriUtility(IApplicationPathResolver pathResolver, IUmbracoUriMapper uriMapper)` — stores both and delegates every method/property to them.
- **Old constructor**: `[Obsolete("Use the constructor with IApplicationPathResolver and IUmbracoUriMapper. Scheduled for removal in Umbraco 19.")] public UriUtility(IHostingEnvironment hostingEnvironment)` — calls the new constructor via `: this(...)` using `StaticServiceProvider.Instance.GetRequiredService<T>()` for each dependency. This matches the documented pattern in `CLAUDE.md` §5.1.
- **Static fields `_appPath` and `_appPathPrefix` are deleted.**
- **Instance properties and methods delegate** to the injected services:
  - `AppPath` → `pathResolver.AppPath`
  - `AppPathPrefix` → `pathResolver.AppPathPrefix`
  - `ToAbsolute` → `pathResolver.ToAbsolute`
  - `ToAppRelative` → `pathResolver.ToAppRelative`
  - `ResolveUrl` → `pathResolver.ResolveUrl`
  - `UriFromUmbraco`, `MediaUriFromUmbraco`, `UriToUmbraco`, `ToFullUrl` → `uriMapper.*`
- **Internal `SetAppDomainAppVirtualPath` / `ResetAppDomainAppVirtualPath`**: marked `[Obsolete("... Scheduled for removal in Umbraco 19.")]`. These methods exist for tests only (comment at line 53 confirms). They forward to an internal seam on `ApplicationPathResolver` that permits re-seating the path. New tests use `ApplicationPathResolver` directly and avoid these methods entirely. Once no test calls them they can be removed per the repo's obsolete-removal policy.
- **Nullability change**: `AppPath` and `AppPathPrefix` are declared `string?` today but in practice are non-null after construction. The new resolver exposes them as non-null `string`. To keep binary compat, `UriUtility.AppPath` and `AppPathPrefix` stay typed `string?` but their runtime value is non-null — i.e., this is purely an API-sugar win inside `ApplicationPathResolver`, with no consumer-visible change.

### 3.3 DI registration

In `src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs` (current registration at line 208 is `Services.AddSingleton<UriUtility>();`):

```csharp
Services.AddSingleton<IApplicationPathResolver, ApplicationPathResolver>();
Services.AddSingleton<IUmbracoUriMapper, UmbracoUriMapper>();
Services.AddSingleton<UriUtility>();
```

All singletons — consistent with current behavior. `UriUtility`'s new constructor resolves the two interfaces from DI normally.

### 3.4 Call graph after the refactor

```
Legacy consumer (e.g., DefaultUrlProvider, UmbracoContext, ~15 sites)
    └── UriUtility (facade, singleton)
          ├── IApplicationPathResolver (singleton)
          │     └── IHostingEnvironment
          └── IUmbracoUriMapper (singleton)
                └── IApplicationPathResolver (shared)

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
- **Instance-isolation test**: construct two `ApplicationPathResolver` instances with distinct `IHostingEnvironment` values (`"/"` and `"/foo"`); assert each reports its own `AppPath`/`AppPathPrefix` and that mutating one (or disposing) does not affect the other. This is the test that locks in the bug fix.

**`UmbracoUriMapperTests.cs`** (same directory)
- Covers `UriFromUmbraco`, `MediaUriFromUmbraco`, `UriToUmbraco`, `ToFullUrl` with a stub or mock `IApplicationPathResolver`.

### 4.3 Coverage target

If any existing test exercises a code path that, post-refactor, routes through the new class, the corresponding assertion should appear in the new class's test file too — so the narrower class has standalone coverage even if the facade is later retired.

## 5. Back-compat checklist

Matches `CLAUDE.md` §5 rules:

- [x] Old constructor marked `[Obsolete]` with "Scheduled for removal in Umbraco 19." (current major is 17, per `version.json`).
- [x] Old constructor delegates to new via `: this(...)`.
- [x] New dependencies resolved via `StaticServiceProvider.Instance.GetRequiredService<T>()` in the old constructor only.
- [x] DI registers the new constructor (via the two interfaces); old constructor is external-only.
- [x] `Set/ResetAppDomainAppVirtualPath` marked `[Obsolete]` with removal schedule; no internal callers remain apart from existing tests.
- [x] Suppress `CS0618` where needed (e.g., inside the facade's delegation to obsolete test hooks).

## 6. Risks

- **Test-only state-reset hooks**: if any existing `UriUtilityTests` depend on calling `SetAppDomainAppVirtualPath` after construction to simulate a different app path, those tests must continue passing via the obsolete forwarding path. The forwarding path must actually mutate the underlying resolver, which requires the resolver to expose an `internal` setter. This is acceptable: it is used only by the obsolete facade method, which disappears in v19.
- **Nullability mismatch** between the facade (`string?`) and the new interface (`string`): handled by keeping the facade signature unchanged.
- **Consumer ripple**: none expected — all 15 internal consumers inject `UriUtility`, which still works.
- **`StaticServiceProvider` growth**: adds exactly one site, inside an obsolete constructor (the documented exception in the recently completed arch review, Finding R-1).

## 7. Open questions

None at design time. All resolved during brainstorming:

- Scope: option C — extract URI parsing vs. app-path logic.
- Facade: keep `UriUtility` sealed and delegating, do not obsolete the class.
- Old constructor: obsolete.
- Old `Set/Reset` internal methods: obsolete.

## 8. Files touched

**New (Core)**
- `src/Umbraco.Core/Routing/IApplicationPathResolver.cs`
- `src/Umbraco.Core/Routing/ApplicationPathResolver.cs`
- `src/Umbraco.Core/Routing/IUmbracoUriMapper.cs`
- `src/Umbraco.Core/Routing/UmbracoUriMapper.cs`

**Modified**
- `src/Umbraco.Core/Routing/UriUtility.cs` — facade rewrite.
- `src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs` — add two singleton registrations alongside existing `UriUtility`.

**New (tests)**
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/ApplicationPathResolverTests.cs`
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UmbracoUriMapperTests.cs`

**Unchanged but verified**
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UriUtilityTests.cs` — must still pass.
- `tests/Umbraco.Tests.UnitTests/Umbraco.Core/CoreThings/UriUtilityCoreTests.cs` — must still pass.

## 9. Success criteria

1. All existing tests in `UriUtilityTests.cs` and `UriUtilityCoreTests.cs` pass unchanged.
2. `ApplicationPathResolverTests.cs` contains an instance-isolation test that would have failed against the old static-field implementation.
3. Full solution builds with `<Nullable>enable</Nullable>` and `<WarningsAsErrors>nullable</WarningsAsErrors>` (as set in `Directory.Build.props`).
4. No new `StaticServiceProvider` call sites outside the one obsolete-constructor delegation.
5. `grep -n "static" src/Umbraco.Core/Routing/UriUtility.cs` returns no field declarations.
