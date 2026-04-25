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
    /// <param name="uriMapper">The URI mapper this facade delegates to.</param>
    public UriUtility(IUmbracoUriMapper uriMapper)
    {
        ArgumentNullException.ThrowIfNull(uriMapper);
        _uriMapper = uriMapper;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="UriUtility" /> class.
    /// </summary>
    /// <param name="hostingEnvironment">The hosting environment from which the application virtual path is read.</param>
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
    /// <param name="url">The relative URL.</param>
    /// <returns>The absolute URL with the application path prefix.</returns>
    public string ToAbsolute(string url) => _uriMapper.PathResolver.ToAbsolute(url);

    /// <summary>
    ///     Converts a virtual path to an application-relative path by stripping the virtual directory if present.
    /// </summary>
    /// <param name="virtualPath">The virtual path.</param>
    /// <returns>The application-relative path.</returns>
    public string ToAppRelative(string virtualPath) => _uriMapper.PathResolver.ToAppRelative(virtualPath);

    /// <summary>
    ///     Resolves a relative URL to an absolute URL.
    /// </summary>
    /// <param name="relativeUrl">The relative URL to resolve.</param>
    /// <returns>The resolved URL.</returns>
    public string ResolveUrl(string relativeUrl) => _uriMapper.PathResolver.ResolveUrl(relativeUrl);

    /// <summary>
    ///     Maps an internal Umbraco URI to a public URI with virtual directory and appropriate suffixes.
    /// </summary>
    /// <param name="uri">The internal Umbraco URI.</param>
    /// <param name="requestConfig">The request handler settings.</param>
    /// <returns>The public URI.</returns>
    public Uri UriFromUmbraco(Uri uri, RequestHandlerSettings requestConfig)
        => _uriMapper.UriFromUmbraco(uri, requestConfig);

    /// <summary>
    ///     Maps a media Umbraco URI to a public URI with virtual directory.
    /// </summary>
    /// <param name="uri">The media URI.</param>
    /// <returns>The public media URI.</returns>
    public Uri MediaUriFromUmbraco(Uri uri) => _uriMapper.MediaUriFromUmbraco(uri);

    /// <summary>
    ///     Maps a public URI to an internal Umbraco URI without virtual directory, lowercased.
    /// </summary>
    /// <param name="uri">The public URI.</param>
    /// <returns>The internal Umbraco URI.</returns>
    public Uri UriToUmbraco(Uri uri) => _uriMapper.UriToUmbraco(uri);

    private static IUmbracoUriMapper BuildMapper(IHostingEnvironment hostingEnvironment)
    {
        ArgumentNullException.ThrowIfNull(hostingEnvironment);
        var resolver = new ApplicationPathResolver(hostingEnvironment);
        return new UmbracoUriMapper(resolver);
    }
}
