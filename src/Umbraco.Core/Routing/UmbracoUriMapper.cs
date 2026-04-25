using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Extensions;

namespace Umbraco.Cms.Core.Routing;

/// <summary>
///     Default <see cref="IUmbracoUriMapper" /> implementation.
/// </summary>
public sealed class UmbracoUriMapper : IUmbracoUriMapper
{
    private readonly IApplicationPathResolver _pathResolver;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UmbracoUriMapper" /> class.
    /// </summary>
    /// <param name="pathResolver">The application path resolver used for path-based transforms.</param>
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
    /// <param name="absolutePath">An absolute path that starts with '/'.</param>
    /// <param name="currentRequestUrl">The current request URL.</param>
    /// <returns>The absolute URI.</returns>
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
