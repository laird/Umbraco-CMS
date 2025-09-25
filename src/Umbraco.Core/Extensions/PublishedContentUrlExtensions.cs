// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Umbraco.Extensions.PublishedContent;

/// <summary>
/// Extension methods for IPublishedContent URL generation operations.
/// </summary>
public static class PublishedContentUrlExtensions
{
    /// <summary>
    /// Gets the URL of the content item.
    /// </summary>
    /// <param name="content">The content item.</param>
    /// <param name="publishedUrlProvider">The published URL provider.</param>
    /// <param name="culture">The specific culture to get the URL for.</param>
    /// <param name="mode">The URL mode.</param>
    /// <returns>The URL for the content.</returns>
    public static string Url(
        this IPublishedContent content,
        IPublishedUrlProvider publishedUrlProvider,
        string? culture = null,
        UrlMode mode = UrlMode.Default)
    {
        if (publishedUrlProvider == null)
        {
            throw new InvalidOperationException("Cannot get a URL without an IPublishedUrlProvider.");
        }

        switch (mode)
        {
            case UrlMode.Default:
                return publishedUrlProvider.GetUrl(content, mode, culture);
            case UrlMode.Relative:
            case UrlMode.Absolute:
                return publishedUrlProvider.GetUrl(content, mode, culture);
            case UrlMode.Auto:
                // This is deprecated but kept for compatibility
                var result = publishedUrlProvider.GetUrl(content, UrlMode.Default, culture);
                if (result.StartsWith("http://") || result.StartsWith("https://"))
                {
                    return result;
                }
                var current = publishedUrlProvider.GetUrl(content, UrlMode.Relative, culture);
                return current?.Equals(result) == true
                    ? publishedUrlProvider.GetUrl(content, UrlMode.Absolute, culture)
                    : result;
            default:
                throw new NotSupportedException($"UrlMode {mode} is not supported.");
        }
    }
}