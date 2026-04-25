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
    public void ResolveUrl_AbsoluteSchemeUrl_ReturnedUnchanged()
    {
        var resolver = BuildResolver("/vdir");

        Assert.AreEqual("https://example.com/page", resolver.ResolveUrl("https://example.com/page"));
    }

    [Test]
    public void ResolveUrl_PlainRelative_PrependsPrefix()
    {
        var resolver = BuildResolver("/vdir");

        Assert.AreEqual("/vdir/foo/bar", resolver.ResolveUrl("foo/bar"));
    }

    [Test]
    public void ResolveUrl_NullInput_ThrowsArgumentNullException()
    {
        var resolver = BuildResolver("/");

        Assert.Throws<ArgumentNullException>(() => resolver.ResolveUrl(null!));
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
