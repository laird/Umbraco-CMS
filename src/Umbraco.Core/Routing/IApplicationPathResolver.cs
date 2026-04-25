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
