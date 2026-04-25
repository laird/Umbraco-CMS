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
    ///     Gets the application path resolver this mapper was constructed with.
    /// </summary>
    /// <remarks>
    ///     Exposed so that consumers of the mapper do not need a separately injected resolver instance.
    /// </remarks>
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
