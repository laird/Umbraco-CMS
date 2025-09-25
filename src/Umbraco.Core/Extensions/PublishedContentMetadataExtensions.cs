// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Extensions.PublishedContent;

/// <summary>
/// Extension methods for IPublishedContent metadata and authorship operations.
/// </summary>
public static class PublishedContentMetadataExtensions
{
    /// <summary>
    /// Gets the name of the content creator.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="userService">The user service.</param>
    /// <returns>The name of the content creator.</returns>
    public static string GetCreatorName(this IPublishedContent content, IUserService userService)
    {
        var creator = userService.GetUserById(content.CreatorId);
        return creator?.Name ?? string.Empty;
    }

    /// <summary>
    /// Gets the name of the content writer (last editor).
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="userService">The user service.</param>
    /// <returns>The name of the content writer.</returns>
    public static string GetWriterName(this IPublishedContent content, IUserService userService)
    {
        var writer = userService.GetUserById(content.WriterId);
        return writer?.Name ?? string.Empty;
    }
}