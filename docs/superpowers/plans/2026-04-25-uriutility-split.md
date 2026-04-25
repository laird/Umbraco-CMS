# UriUtility Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `Umbraco.Core/Routing/UriUtility` into a strictly-immutable `ApplicationPathResolver` and a stateless `UmbracoUriMapper`, with `UriUtility` retained as a binary-compatible facade.

**Architecture:** Two new public interfaces (`IApplicationPathResolver`, `IUmbracoUriMapper`) with concrete singleton implementations. `UriUtility` is rewritten to inject only `IUmbracoUriMapper` (the mapper exposes its `PathResolver`), eliminating the static-mutable-state bug at lines 13–14 of the existing file. Old constructor `(IHostingEnvironment)` stays as `[Obsolete]` and constructs its dependencies inline (no `StaticServiceProvider`). Existing tests are the regression guard; new tests cover the narrower classes plus a constructor-equivalence assertion.

**Tech Stack:** .NET 10, NUnit, Moq, NPoco, ASP.NET Core. Repository: `/Users/Laird.Popkin/src/Umbraco-CMS`. Working branch: `planfirst`.

---

## Execution Strategy

This plan is executed via `superpowers:subagent-driven-development`. To keep wall-clock time and subagent-dispatch count reasonable on a large .NET solution (each `dotnet build` is slow), tasks are grouped into **execution batches**. Each batch is one implementer dispatch followed by a spec-compliance review and a code-quality review.

| Batch | Tasks | Rationale |
|---|---|---|
| A | 1, 3 | Both are mechanical interface files. One commit per task is preserved. |
| B | 2 | TDD task — write failing tests, port implementation, verify. Substantial enough to stand alone. |
| C | 4 | Same shape as Batch B for the mapper. |
| D | 5, 6, 7 | The facade rewrite, DI registration, and equivalence test must land together to keep the build green. Tasks 5+6 share a single commit per the plan; Task 7 commits separately. |
| E | 8 | Verification only. Implementer runs the suite and the verification commands; no code changes. Single review pass — code-quality review is skipped (no new code). |
| Final | — | One final code-quality review of the entire branch diff. |

**Total dispatches**: 4 batches × 3 (implementer + 2 reviews) + Batch E (1 implementer + 1 spec review) + 1 final review = **15 subagent dispatches**.

**Operational notes**:
- Working branch is `planfirst`; no separate git worktree is set up — the branch is already isolated from `main` and from any other in-progress work.
- All subagents use a standard-capability model. The codebase conventions (binary-compat rules in `CLAUDE.md` §5, NUnit + Moq, implicit usings) require real reasoning; a fast/cheap model is too constrained.
- Each batch's implementer commits per the plan's commit instructions inside its tasks. Reviews observe the commits via `git log`/`git diff`.
- If any review surfaces unfixable disagreement, the controller (the human-facing session) escalates rather than letting the subagent thrash.

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/Umbraco.Core/Routing/IApplicationPathResolver.cs` | Interface for app-path state and path-relative URL transforms. | Create |
| `src/Umbraco.Core/Routing/ApplicationPathResolver.cs` | Immutable implementation; reads `IHostingEnvironment.ApplicationVirtualPath` once at construction. | Create |
| `src/Umbraco.Core/Routing/IUmbracoUriMapper.cs` | Interface for Umbraco-internal ↔ public URI transforms; exposes underlying `IApplicationPathResolver`. | Create |
| `src/Umbraco.Core/Routing/UmbracoUriMapper.cs` | Stateless implementation depending on `IApplicationPathResolver`. | Create |
| `src/Umbraco.Core/Routing/UriUtility.cs` | Becomes a thin facade over `IUmbracoUriMapper`. Static fields and `Set/Reset…` methods removed. | Modify |
| `src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs` (line 208) | Register both new interfaces as singletons; keep existing `UriUtility` registration. | Modify |
| `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/ApplicationPathResolverTests.cs` | Direct tests for the resolver including instance-isolation. | Create |
| `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UmbracoUriMapperTests.cs` | Direct tests for the mapper. | Create |
| `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UriUtilityTests.cs` | Regression guard. Add a constructor-equivalence test. | Modify |

---

## Task 1: Add `IApplicationPathResolver` interface

**Files:**
- Create: `src/Umbraco.Core/Routing/IApplicationPathResolver.cs`

- [ ] **Step 1: Create the interface file**

```csharp
// Copyright (c) Umbraco.
// See LICENSE for more details.

namespace Umbraco.Cms.Core.Routing;

/// <summary>
///     Resolves application-relative paths and the application's virtual path prefix.
/// </summary>
/// <remarks>
///     Implementations are immutable after construction and safe to use as singletons.
/// </remarks>
public interface IApplicationPathResolver
{
    /// <summary>
    ///     Gets the application path. Always non-null; "/" or "/foo".
    /// </summary>
    string AppPath { get; }

    /// <summary>
    ///     Gets the application path prefix. Always non-null; "" or "/foo".
    /// </summary>
    string AppPathPrefix { get; }

    /// <summary>
    ///     Converts a relative URL to an absolute URL by prepending the application path prefix.
    /// </summary>
    /// <param name="url">The relative URL.</param>
    /// <returns>The absolute URL with the application path prefix.</returns>
    string ToAbsolute(string url);

    /// <summary>
    ///     Converts a virtual path to an application-relative path by stripping the virtual directory if present.
    /// </summary>
    /// <param name="virtualPath">The virtual path.</param>
    /// <returns>The application-relative path.</returns>
    string ToAppRelative(string virtualPath);

    /// <summary>
    ///     Resolves a relative URL to an absolute URL.
    /// </summary>
    /// <remarks>
    ///     If browsing http://example.com/sub/page1.aspx then ResolveUrl("page2.aspx") returns "/page2.aspx".
    /// </remarks>
    string ResolveUrl(string relativeUrl);
}
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build src/Umbraco.Core/Umbraco.Core.csproj`
Expected: Build succeeded, 0 Errors. (Warnings about an unused interface are fine — there are no implementations yet.)

- [ ] **Step 3: Commit**

```bash
git add src/Umbraco.Core/Routing/IApplicationPathResolver.cs
git commit -m "feat(routing): add IApplicationPathResolver interface

Defines the contract for the new path resolver that will replace
the static-field state in UriUtility."
```

---

## Task 2: Implement `ApplicationPathResolver` with TDD

**Files:**
- Create: `src/Umbraco.Core/Routing/ApplicationPathResolver.cs`
- Create: `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/ApplicationPathResolverTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/ApplicationPathResolverTests.cs`:

```csharp
// Copyright (c) Umbraco.
// See LICENSE for more details.

using Moq;
using NUnit.Framework;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.Routing;

namespace Umbraco.Cms.Tests.UnitTests.Umbraco.Core.Routing;

[TestFixture]
public class ApplicationPathResolverTests
{
    [TestCase("/", "/", "")]
    [TestCase("/foo", "/foo", "/foo")]
    [TestCase("/foo/bar", "/foo/bar", "/foo/bar")]
    public void Constructor_SetsAppPathAndPrefix(string virtualPath, string expectedAppPath, string expectedPrefix)
    {
        var resolver = BuildResolver(virtualPath);

        Assert.AreEqual(expectedAppPath, resolver.AppPath);
        Assert.AreEqual(expectedPrefix, resolver.AppPathPrefix);
    }

    [Test]
    public void Constructor_NullVirtualPath_NormalizesToSlash()
    {
        var hosting = new Mock<IHostingEnvironment>();
        hosting.Setup(x => x.ApplicationVirtualPath).Returns((string)null!);

        var resolver = new ApplicationPathResolver(hosting.Object);

        Assert.AreEqual("/", resolver.AppPath);
        Assert.AreEqual(string.Empty, resolver.AppPathPrefix);
    }

    [TestCase("/", "/", "/")]
    [TestCase("/", "/foo", "/foo")]
    [TestCase("/", "~/foo", "/foo")]
    [TestCase("/vdir", "/", "/vdir/")]
    [TestCase("/vdir", "/foo", "/vdir/foo")]
    [TestCase("/vdir", "/foo/", "/vdir/foo/")]
    [TestCase("/vdir", "~/foo", "/vdir/foo")]
    public void ToAbsolute_PrependsAppPathPrefix(string virtualPath, string sourceUrl, string expected)
    {
        var resolver = BuildResolver(virtualPath);

        Assert.AreEqual(expected, resolver.ToAbsolute(sourceUrl));
    }

    [TestCase("/", "/", "/")]
    [TestCase("/", "/foo", "/foo")]
    [TestCase("/", "/foo/", "/foo/")]
    [TestCase("/vdir", "/vdir", "/")]
    [TestCase("/vdir", "/vdir/", "/")]
    [TestCase("/vdir", "/vdir/foo", "/foo")]
    [TestCase("/vdir", "/vdir/foo/", "/foo/")]
    public void ToAppRelative_StripsAppPathPrefix(string virtualPath, string sourceUrl, string expected)
    {
        var resolver = BuildResolver(virtualPath);

        Assert.AreEqual(expected, resolver.ToAppRelative(sourceUrl));
    }

    [Test]
    public void ResolveUrl_AbsoluteUrl_ReturnedUnchanged()
    {
        var resolver = BuildResolver("/vdir");

        Assert.AreEqual("/already/absolute", resolver.ResolveUrl("/already/absolute"));
    }

    [Test]
    public void ResolveUrl_TildeRelative_PrependsPrefix()
    {
        var resolver = BuildResolver("/vdir");

        Assert.AreEqual("/vdir/foo", resolver.ResolveUrl("~/foo"));
    }

    [Test]
    public void TwoInstances_DistinctVirtualPaths_DoNotShareState()
    {
        // Locks in the bug fix: pre-refactor, both instances would silently share static fields.
        var rootResolver = BuildResolver("/");
        var vdirResolver = BuildResolver("/vdir");

        Assert.AreEqual("/", rootResolver.AppPath);
        Assert.AreEqual(string.Empty, rootResolver.AppPathPrefix);
        Assert.AreEqual("/vdir", vdirResolver.AppPath);
        Assert.AreEqual("/vdir", vdirResolver.AppPathPrefix);

        // Mutual independence under operations:
        Assert.AreEqual("/foo", rootResolver.ToAbsolute("/foo"));
        Assert.AreEqual("/vdir/foo", vdirResolver.ToAbsolute("/foo"));
    }

    private static ApplicationPathResolver BuildResolver(string virtualPath)
    {
        var hosting = new Mock<IHostingEnvironment>();
        hosting.Setup(x => x.ApplicationVirtualPath).Returns(virtualPath);
        return new ApplicationPathResolver(hosting.Object);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet build tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj`
Expected: Compile error CS0246 — type or namespace `ApplicationPathResolver` not found.

- [ ] **Step 3: Implement `ApplicationPathResolver`**

Create `src/Umbraco.Core/Routing/ApplicationPathResolver.cs`:

```csharp
// Copyright (c) Umbraco.
// See LICENSE for more details.

using System.Text;
using Umbraco.Cms.Core.Hosting;

namespace Umbraco.Cms.Core.Routing;

/// <summary>
///     Default <see cref="IApplicationPathResolver" /> implementation.
///     Reads the application virtual path once at construction and is immutable thereafter.
/// </summary>
public sealed class ApplicationPathResolver : IApplicationPathResolver
{
    private readonly string _appPath;
    private readonly string _appPathPrefix;

    public ApplicationPathResolver(IHostingEnvironment hostingEnvironment)
    {
        ArgumentNullException.ThrowIfNull(hostingEnvironment);

        _appPath = hostingEnvironment.ApplicationVirtualPath ?? "/";
        _appPathPrefix = _appPath == "/" ? string.Empty : _appPath;
    }

    /// <inheritdoc />
    public string AppPath => _appPath;

    /// <inheritdoc />
    public string AppPathPrefix => _appPathPrefix;

    /// <inheritdoc />
    public string ToAbsolute(string url)
    {
        url = url.TrimStart(Constants.CharArrays.Tilde);
        return _appPathPrefix + url;
    }

    /// <inheritdoc />
    public string ToAppRelative(string virtualPath)
    {
        if (_appPathPrefix.Length > 0
            && virtualPath.InvariantStartsWith(_appPathPrefix)
            && (virtualPath.Length == _appPathPrefix.Length
                || virtualPath[_appPathPrefix.Length] == '/'))
        {
            virtualPath = virtualPath[_appPathPrefix.Length..];
        }

        if (virtualPath.Length == 0)
        {
            virtualPath = "/";
        }

        return virtualPath;
    }

    /// <inheritdoc />
    public string ResolveUrl(string relativeUrl)
    {
        ArgumentNullException.ThrowIfNull(relativeUrl);

        if (relativeUrl.Length == 0 || relativeUrl[0] == '/' || relativeUrl[0] == '\\')
        {
            return relativeUrl;
        }

        var idxOfScheme = relativeUrl.IndexOf("://", StringComparison.Ordinal);
        if (idxOfScheme != -1)
        {
            var idxOfQM = relativeUrl.IndexOf('?', StringComparison.Ordinal);
            if (idxOfQM == -1 || idxOfQM > idxOfScheme)
            {
                return relativeUrl;
            }
        }

        var sbUrl = new StringBuilder();
        sbUrl.Append(_appPathPrefix);
        if (sbUrl.Length == 0 || sbUrl[^1] != '/')
        {
            sbUrl.Append('/');
        }

        var foundQM = false;
        bool foundSlash;
        if (relativeUrl.Length > 1
            && relativeUrl[0] == '~'
            && (relativeUrl[1] == '/' || relativeUrl[1] == '\\'))
        {
            relativeUrl = relativeUrl[2..];
            foundSlash = true;
        }
        else
        {
            foundSlash = false;
        }

        foreach (var c in relativeUrl)
        {
            if (!foundQM)
            {
                if (c == '?')
                {
                    foundQM = true;
                }
                else
                {
                    if (c == '/' || c == '\\')
                    {
                        if (foundSlash)
                        {
                            continue;
                        }

                        sbUrl.Append('/');
                        foundSlash = true;
                        continue;
                    }

                    if (foundSlash)
                    {
                        foundSlash = false;
                    }
                }
            }

            sbUrl.Append(c);
        }

        return sbUrl.ToString();
    }
}
```

Note: `Umbraco.Extensions` is included implicitly via `<ImplicitUsings>` set in `Directory.Build.props`; `InvariantStartsWith` is an extension method on `string` from that namespace.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj --filter "FullyQualifiedName~ApplicationPathResolverTests"`
Expected: All tests pass (8 cases for `Constructor_SetsAppPathAndPrefix`, 1 for null normalization, 7 for `ToAbsolute`, 7 for `ToAppRelative`, 2 for `ResolveUrl`, 1 for `TwoInstances_DistinctVirtualPaths_DoNotShareState`).

- [ ] **Step 5: Commit**

```bash
git add src/Umbraco.Core/Routing/ApplicationPathResolver.cs \
        tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/ApplicationPathResolverTests.cs
git commit -m "feat(routing): add ApplicationPathResolver with instance state

Replaces static-field state with immutable instance fields. Includes a
test that constructs two resolvers with distinct virtual paths and
asserts they do not share state — a guard against the previous bug."
```

---

## Task 3: Add `IUmbracoUriMapper` interface

**Files:**
- Create: `src/Umbraco.Core/Routing/IUmbracoUriMapper.cs`

- [ ] **Step 1: Create the interface file**

```csharp
// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Configuration.Models;

namespace Umbraco.Cms.Core.Routing;

/// <summary>
///     Maps URIs between Umbraco-internal form (no virtual directory, lowercased) and public form.
/// </summary>
/// <remarks>
///     Implementations are stateless aside from their injected <see cref="IApplicationPathResolver" />
///     and safe to use as singletons.
/// </remarks>
public interface IUmbracoUriMapper
{
    /// <summary>
    ///     The application path resolver this mapper was constructed with. Exposed so that consumers
    ///     of the mapper do not need a separately injected resolver instance.
    /// </summary>
    IApplicationPathResolver PathResolver { get; }

    /// <summary>
    ///     Maps an internal Umbraco URI to a public URI with virtual directory and appropriate suffixes.
    /// </summary>
    Uri UriFromUmbraco(Uri uri, RequestHandlerSettings requestConfig);

    /// <summary>
    ///     Maps a media Umbraco URI to a public URI with virtual directory.
    /// </summary>
    Uri MediaUriFromUmbraco(Uri uri);

    /// <summary>
    ///     Maps a public URI to an internal Umbraco URI without virtual directory, lowercased.
    /// </summary>
    Uri UriToUmbraco(Uri uri);
}
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build src/Umbraco.Core/Umbraco.Core.csproj`
Expected: Build succeeded, 0 Errors.

- [ ] **Step 3: Commit**

```bash
git add src/Umbraco.Core/Routing/IUmbracoUriMapper.cs
git commit -m "feat(routing): add IUmbracoUriMapper interface

Defines the URI-mapping contract that will be split off from UriUtility.
Exposes its own PathResolver so consumers don't need a separately
injected IApplicationPathResolver."
```

---

## Task 4: Implement `UmbracoUriMapper` with TDD

**Files:**
- Create: `src/Umbraco.Core/Routing/UmbracoUriMapper.cs`
- Create: `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UmbracoUriMapperTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UmbracoUriMapperTests.cs`:

```csharp
// Copyright (c) Umbraco.
// See LICENSE for more details.

using Moq;
using NUnit.Framework;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.Routing;

namespace Umbraco.Cms.Tests.UnitTests.Umbraco.Core.Routing;

[TestFixture]
public class UmbracoUriMapperTests
{
    [Test]
    public void PathResolver_ReturnsInjectedInstance()
    {
        var resolver = new ApplicationPathResolver(BuildHosting("/"));
        var mapper = new UmbracoUriMapper(resolver);

        Assert.AreSame(resolver, mapper.PathResolver);
    }

    // Mirrors the cases in UriUtilityTests.Uri_To_Umbraco
    [TestCase("http://LocalHost/", "http://localhost/")]
    [TestCase("http://LocalHost/?x=y", "http://localhost/?x=y")]
    [TestCase("http://LocalHost/Home", "http://localhost/home")]
    [TestCase("http://LocalHost/Home/Sub1", "http://localhost/home/sub1")]
    [TestCase("http://LocalHost/Home/", "http://localhost/home")]
    [TestCase("http://LocalHost/Home/////", "http://localhost/home")]
    [TestCase("http://LocalHost/Home/?x=y", "http://localhost/home?x=y")]
    [TestCase("http://LocalHost/Home/Sub1/", "http://localhost/home/sub1")]
    [TestCase("http://LocalHost/Home/Sub1/?x=y", "http://localhost/home/sub1?x=y")]
    public void UriToUmbraco_StripsVdirAndLowercases(string sourceUrl, string expectedUrl)
    {
        var mapper = BuildMapper("/");
        var result = mapper.UriToUmbraco(new Uri(sourceUrl));

        Assert.AreEqual(new Uri(expectedUrl).ToString(), result.ToString());
    }

    [TestCase("/", "/", false)]
    [TestCase("/home", "/home", false)]
    [TestCase("/", "/", true)]
    [TestCase("/home", "/home/", true)]
    [TestCase("/home/sub1", "/home/sub1/", true)]
    public void UriFromUmbraco_AddsTrailingSlashWhenConfigured(string sourceUrl, string expectedUrl, bool trailingSlash)
    {
        var mapper = BuildMapper("/");
        var settings = new RequestHandlerSettings { AddTrailingSlash = trailingSlash };

        var result = mapper.UriFromUmbraco(new Uri(sourceUrl, UriKind.Relative), settings);

        Assert.AreEqual(new Uri(expectedUrl, UriKind.Relative).ToString(), result.ToString());
    }

    [Test]
    public void MediaUriFromUmbraco_PrependsVdir()
    {
        var mapper = BuildMapper("/vdir");
        var result = mapper.MediaUriFromUmbraco(new Uri("/media/image.jpg", UriKind.Relative));

        Assert.AreEqual("/vdir/media/image.jpg", result.ToString());
    }

    private static UmbracoUriMapper BuildMapper(string virtualPath)
        => new UmbracoUriMapper(new ApplicationPathResolver(BuildHosting(virtualPath)));

    private static IHostingEnvironment BuildHosting(string virtualPath)
    {
        var hosting = new Mock<IHostingEnvironment>();
        hosting.Setup(x => x.ApplicationVirtualPath).Returns(virtualPath);
        return hosting.Object;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet build tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj`
Expected: Compile error CS0246 — `UmbracoUriMapper` not found.

- [ ] **Step 3: Implement `UmbracoUriMapper`**

Create `src/Umbraco.Core/Routing/UmbracoUriMapper.cs`:

```csharp
// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Extensions;

namespace Umbraco.Cms.Core.Routing;

/// <summary>
///     Default <see cref="IUmbracoUriMapper" /> implementation.
/// </summary>
public sealed class UmbracoUriMapper : IUmbracoUriMapper
{
    private readonly IApplicationPathResolver _pathResolver;

    public UmbracoUriMapper(IApplicationPathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        _pathResolver = pathResolver;
    }

    /// <inheritdoc />
    public IApplicationPathResolver PathResolver => _pathResolver;

    /// <inheritdoc />
    public Uri UriFromUmbraco(Uri uri, RequestHandlerSettings requestConfig)
    {
        var path = uri.GetSafeAbsolutePath();

        if (path != "/" && requestConfig.AddTrailingSlash)
        {
            path = path.EnsureEndsWith("/");
        }

        path = _pathResolver.ToAbsolute(path);

        return uri.Rewrite(path);
    }

    /// <inheritdoc />
    public Uri MediaUriFromUmbraco(Uri uri)
    {
        var path = uri.GetSafeAbsolutePath();
        path = _pathResolver.ToAbsolute(path);
        return uri.Rewrite(path);
    }

    /// <inheritdoc />
    public Uri UriToUmbraco(Uri uri)
    {
        // TODO: This is critical code that executes on every request, we should
        // look into if all of this is necessary? not really sure we need ToLower?
        var path = uri.GetSafeAbsolutePath();

        path = path.ToLower();
        path = _pathResolver.ToAppRelative(path);

        if (path != "/")
        {
            path = path.TrimEnd(Constants.CharArrays.ForwardSlash);

            if (path == string.Empty)
            {
                path = "/";
            }
        }

        return uri.Rewrite(path);
    }

    /// <summary>
    ///     Returns a full URL with host, port, etc. Internal — exposed on the facade only for legacy callers.
    /// </summary>
    internal Uri ToFullUrl(string absolutePath, Uri currentRequestUrl)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            throw new ArgumentNullException(nameof(absolutePath));
        }

        if (!absolutePath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new FormatException("The absolutePath specified does not start with a '/'");
        }

        return new Uri(absolutePath, UriKind.Relative).MakeAbsolute(currentRequestUrl);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj --filter "FullyQualifiedName~UmbracoUriMapperTests"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Umbraco.Core/Routing/UmbracoUriMapper.cs \
        tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UmbracoUriMapperTests.cs
git commit -m "feat(routing): add UmbracoUriMapper

Stateless URI transforms; depends on IApplicationPathResolver and exposes
it via the PathResolver property so consumers don't need to inject it
separately."
```

---

## Task 5: Rewrite `UriUtility` as a facade

**Files:**
- Modify: `src/Umbraco.Core/Routing/UriUtility.cs` (entire file, 273 lines → ~115)

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `src/Umbraco.Core/Routing/UriUtility.cs` with:

```csharp
// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Hosting;

namespace Umbraco.Cms.Core.Routing;

/// <summary>
///     Provides utilities for manipulating URIs in the Umbraco routing context.
/// </summary>
/// <remarks>
///     This class is a thin facade over <see cref="IUmbracoUriMapper" /> and the
///     <see cref="IApplicationPathResolver" /> that the mapper exposes via
///     <see cref="IUmbracoUriMapper.PathResolver" />. New consumers should inject
///     those interfaces directly.
/// </remarks>
public sealed class UriUtility
{
    private readonly IUmbracoUriMapper _uriMapper;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UriUtility" /> class.
    /// </summary>
    public UriUtility(IUmbracoUriMapper uriMapper)
    {
        ArgumentNullException.ThrowIfNull(uriMapper);
        _uriMapper = uriMapper;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="UriUtility" /> class.
    /// </summary>
    /// <remarks>
    ///     Constructs an internal <see cref="ApplicationPathResolver" /> and
    ///     <see cref="UmbracoUriMapper" /> directly without using DI.
    /// </remarks>
    [Obsolete("Use the constructor with IUmbracoUriMapper. Scheduled for removal in Umbraco 19.")]
    public UriUtility(IHostingEnvironment hostingEnvironment)
        : this(BuildMapper(hostingEnvironment))
    {
    }

    /// <summary>
    ///     Gets the application path. Will be "/" or "/foo".
    /// </summary>
    public string? AppPath => _uriMapper.PathResolver.AppPath;

    /// <summary>
    ///     Gets the application path prefix. Will be "" or "/foo".
    /// </summary>
    public string? AppPathPrefix => _uriMapper.PathResolver.AppPathPrefix;

    /// <summary>
    ///     Converts a relative URL to an absolute URL by prepending the application path prefix.
    /// </summary>
    public string ToAbsolute(string url) => _uriMapper.PathResolver.ToAbsolute(url);

    /// <summary>
    ///     Converts a virtual path to an application-relative path by stripping the virtual directory if present.
    /// </summary>
    public string ToAppRelative(string virtualPath) => _uriMapper.PathResolver.ToAppRelative(virtualPath);

    /// <summary>
    ///     Resolves a relative URL to an absolute URL.
    /// </summary>
    public string ResolveUrl(string relativeUrl) => _uriMapper.PathResolver.ResolveUrl(relativeUrl);

    /// <summary>
    ///     Maps an internal Umbraco URI to a public URI with virtual directory and appropriate suffixes.
    /// </summary>
    public Uri UriFromUmbraco(Uri uri, RequestHandlerSettings requestConfig)
        => _uriMapper.UriFromUmbraco(uri, requestConfig);

    /// <summary>
    ///     Maps a media Umbraco URI to a public URI with virtual directory.
    /// </summary>
    public Uri MediaUriFromUmbraco(Uri uri) => _uriMapper.MediaUriFromUmbraco(uri);

    /// <summary>
    ///     Maps a public URI to an internal Umbraco URI without virtual directory, lowercased.
    /// </summary>
    public Uri UriToUmbraco(Uri uri) => _uriMapper.UriToUmbraco(uri);

    /// <summary>
    ///     Returns a full URL with the host, port, etc.
    /// </summary>
    internal Uri ToFullUrl(string absolutePath, Uri currentRequestUrl)
        => ((UmbracoUriMapper)_uriMapper).ToFullUrl(absolutePath, currentRequestUrl);

    private static IUmbracoUriMapper BuildMapper(IHostingEnvironment hostingEnvironment)
    {
        ArgumentNullException.ThrowIfNull(hostingEnvironment);
        var resolver = new ApplicationPathResolver(hostingEnvironment);
        return new UmbracoUriMapper(resolver);
    }
}
```

Notes:
- The internal `ToFullUrl` casts to the concrete `UmbracoUriMapper`. This is acceptable because `ToFullUrl` is itself `internal` and callers within the assembly already know they are getting a `UmbracoUriMapper` (DI registers exactly that concrete type). If a future refactor changes the registered impl, this cast will need to either be replaced with an interface method or a separate internal accessor.
- `AppPath` and `AppPathPrefix` keep `string?` signatures for binary compat. The implicit non-null → nullable widening is benign.
- Deleted from previous version: static fields `_appPath`, `_appPathPrefix`; `SetAppDomainAppVirtualPath`; `ResetAppDomainAppVirtualPath`. Verified by `grep` (in spec section 1) that no caller exists outside the file itself.

- [ ] **Step 2: Build the Core project**

Run: `dotnet build src/Umbraco.Core/Umbraco.Core.csproj`
Expected: Build succeeded, 0 Errors. Possible warning CS0618 from internal call sites that still use the obsolete constructor — there should be none in `src/Umbraco.Core` (the only call to `Reset/Set...` was inside `UriUtility.cs` itself).

- [ ] **Step 3: Verify no callers of the deleted methods remain**

Run: `grep -rn "AppDomainAppVirtualPath\b" src tests --include="*.cs"`
Expected: No matches anywhere except possibly comments. If any production caller appears, stop and add it to this task.

---

## Task 6: Register the new services in DI

**Files:**
- Modify: `src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs:208`

- [ ] **Step 1: Read the registration block**

Run: `sed -n '200,215p' src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs`
This shows the exact context around line 208.

- [ ] **Step 2: Add the two new registrations**

Find the line:

```csharp
            Services.AddSingleton<UriUtility>();
```

Replace it with:

```csharp
            Services.AddSingleton<IApplicationPathResolver, ApplicationPathResolver>();
            Services.AddSingleton<IUmbracoUriMapper, UmbracoUriMapper>();
            Services.AddSingleton<UriUtility>();
```

- [ ] **Step 3: Build the Core project**

Run: `dotnet build src/Umbraco.Core/Umbraco.Core.csproj`
Expected: Build succeeded, 0 Errors.

- [ ] **Step 4: Commit Task 5 + Task 6 together**

```bash
git add src/Umbraco.Core/Routing/UriUtility.cs \
        src/Umbraco.Core/DependencyInjection/UmbracoBuilder.cs
git commit -m "refactor(routing): UriUtility becomes a facade over IUmbracoUriMapper

Static fields and Set/ResetAppDomainAppVirtualPath methods removed —
verified by grep to have no callers anywhere in src/ or tests/.
Old constructor kept as [Obsolete] and constructs its dependencies
inline (no StaticServiceProvider). DI registers IApplicationPathResolver
and IUmbracoUriMapper as singletons alongside the existing UriUtility."
```

---

## Task 7: Add the constructor-equivalence test

**Files:**
- Modify: `tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UriUtilityTests.cs`

- [ ] **Step 1: Append the equivalence test before the closing brace of the class**

Insert this method after the existing `Url_To_App_Relative` method (at line 106, before the `BuildUriUtility` helper):

```csharp
    [TestCase("/")]
    [TestCase("/foo")]
    [TestCase("/foo/bar")]
    [TestCase(null)]
    public void Constructors_AreObservationallyEquivalent(string? virtualPath)
    {
        // Same IHostingEnvironment instance fed to both constructors.
        var hosting = new Mock<IHostingEnvironment>();
        hosting.Setup(x => x.ApplicationVirtualPath).Returns(virtualPath!);

        var resolver = new ApplicationPathResolver(hosting.Object);
        var mapper = new UmbracoUriMapper(resolver);

#pragma warning disable CS0618 // Obsolete ctor — exercised intentionally for the equivalence test.
        var viaObsolete = new UriUtility(hosting.Object);
#pragma warning restore CS0618
        var viaNew = new UriUtility(mapper);

        Assert.AreEqual(viaObsolete.AppPath, viaNew.AppPath);
        Assert.AreEqual(viaObsolete.AppPathPrefix, viaNew.AppPathPrefix);
        Assert.AreEqual(viaObsolete.ToAbsolute("/foo"), viaNew.ToAbsolute("/foo"));
        Assert.AreEqual(viaObsolete.ToAbsolute("~/foo"), viaNew.ToAbsolute("~/foo"));
        Assert.AreEqual(viaObsolete.ToAppRelative("/foo/bar"), viaNew.ToAppRelative("/foo/bar"));
        Assert.AreEqual(viaObsolete.ResolveUrl("~/foo"), viaNew.ResolveUrl("~/foo"));

        var settings = new RequestHandlerSettings { AddTrailingSlash = true };
        Assert.AreEqual(
            viaObsolete.UriFromUmbraco(new Uri("/home", UriKind.Relative), settings).ToString(),
            viaNew.UriFromUmbraco(new Uri("/home", UriKind.Relative), settings).ToString());
        Assert.AreEqual(
            viaObsolete.UriToUmbraco(new Uri("http://example/Home/")).ToString(),
            viaNew.UriToUmbraco(new Uri("http://example/Home/")).ToString());
        Assert.AreEqual(
            viaObsolete.MediaUriFromUmbraco(new Uri("/media/x.jpg", UriKind.Relative)).ToString(),
            viaNew.MediaUriFromUmbraco(new Uri("/media/x.jpg", UriKind.Relative)).ToString());
    }
```

- [ ] **Step 2: Run the regression-guard tests**

Run: `dotnet test tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj --filter "FullyQualifiedName~UriUtilityTests"`
Expected: All existing tests still pass plus the 4 new equivalence test cases pass.

- [ ] **Step 3: Run the full Routing test directory to catch incidental breakage**

Run: `dotnet test tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj --filter "FullyQualifiedName~Umbraco.Core.Routing"`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Umbraco.Tests.UnitTests/Umbraco.Core/Routing/UriUtilityTests.cs
git commit -m "test(routing): assert UriUtility constructors are equivalent

Both the new (IUmbracoUriMapper) and obsolete (IHostingEnvironment)
constructors must produce facades that observe identical AppPath,
AppPathPrefix, ToAbsolute, ToAppRelative, ResolveUrl, UriFromUmbraco,
UriToUmbraco, and MediaUriFromUmbraco for matching inputs."
```

---

## Task 8: Verify the full unit and integration suites

**Files:** none modified

- [ ] **Step 1: Run the entire unit-test project**

Run: `dotnet test tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj`
Expected: All tests pass, 0 failures.

- [ ] **Step 2: Run the integration-test project (default SQLite)**

Run: `dotnet test tests/Umbraco.Tests.Integration/Umbraco.Tests.Integration.csproj`
Expected: All tests pass, 0 failures. This is success criterion §9.6 from the spec.

If integration tests fail and the failure is unrelated to URL/routing/`UriUtility`, document it in the PR description and proceed; if it is related, debug and fix before continuing.

- [ ] **Step 3: Confirm the design-doc success criteria**

Run each verification command exactly:

```bash
# §9.4 — no new StaticServiceProvider sites in UriUtility.cs
grep -rn "StaticServiceProvider" src/Umbraco.Core/Routing/UriUtility.cs || echo "OK: no StaticServiceProvider"

# §9.5 — no static field declarations in UriUtility.cs
grep -E '^\s*(private|public|internal|protected)\s+static\s+\w' src/Umbraco.Core/Routing/UriUtility.cs || echo "OK: no static fields"

# Sanity — the deleted Set/Reset methods are gone
grep -n "AppDomainAppVirtualPath" src/Umbraco.Core/Routing/UriUtility.cs && echo "FAIL: methods still present" || echo "OK: methods removed"
```

Expected: each command prints `OK: …`.

- [ ] **Step 4: Build the full solution to confirm nothing else regressed**

Run: `dotnet build umbraco.sln`
Expected: Build succeeded, 0 Errors. Pre-existing warnings (e.g., other obsolete-attribute usages) are out of scope.

- [ ] **Step 5: Push the branch**

```bash
git push origin planfirst
```

Expected: Push succeeds; remote `planfirst` advances.

---

## Done

The plan is complete when:
- All eight tasks are checked off.
- All commands in Task 8 print `OK: …`.
- The unit and integration suites pass.
- `planfirst` is pushed to origin.

The PR title should be: `Routing: Split UriUtility into ApplicationPathResolver + UmbracoUriMapper`.
