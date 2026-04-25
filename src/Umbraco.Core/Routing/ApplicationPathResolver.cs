using System.Text;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Extensions;

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
