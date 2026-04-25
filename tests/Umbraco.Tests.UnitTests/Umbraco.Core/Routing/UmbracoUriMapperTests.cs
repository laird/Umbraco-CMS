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

    [Test]
    public void Constructor_NullResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UmbracoUriMapper(null!));
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
