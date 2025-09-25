// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;

namespace Umbraco.Extensions.PublishedContent;

/// <summary>
/// Extension methods for IPublishedContent culture and localization operations.
/// </summary>
public static class PublishedContentCultureExtensions
{
    #region Name

    /// <summary>
    /// Gets the name of the content item.
    /// </summary>
    /// <param name="content">The content item.</param>
    /// <param name="variationContextAccessor">The variation context accessor.</param>
    /// <param name="culture">
    /// The specific culture to get the name for. If null is used the current culture is used (Default is null).
    /// </param>
    public static string Name(this IPublishedContent content, IVariationContextAccessor? variationContextAccessor, string? culture = null)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        // invariant has invariant value (whatever the requested culture)
        if (!content.ContentType.VariesByCulture())
        {
            return content.Cultures.TryGetValue(string.Empty, out PublishedCultureInfo? invariantInfos)
                ? invariantInfos.Name
                : string.Empty;
        }

        // handle context culture for variant
        if (culture == null)
        {
            culture = variationContextAccessor?.VariationContext?.Culture ?? string.Empty;
        }

        // get
        return culture != string.Empty && content.Cultures.TryGetValue(culture, out PublishedCultureInfo? infos)
            ? infos.Name
            : string.Empty;
    }

    #endregion

    #region Url Segment

    /// <summary>
    /// Gets the URL segment of the content item.
    /// </summary>
    /// <param name="content">The content item.</param>
    /// <param name="variationContextAccessor">The variation context accessor.</param>
    /// <param name="culture">
    /// The specific culture to get the URL segment for. If null is used the current culture is used (Default is null).
    /// </param>
    [Obsolete("Please use GetUrlSegment() on IDocumentUrlService instead. Scheduled for removal in V16.")]
    public static string? UrlSegment(this IPublishedContent content, IVariationContextAccessor? variationContextAccessor, string? culture = null)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        // invariant has invariant value (whatever the requested culture)
        if (!content.ContentType.VariesByCulture())
        {
            return content.Cultures.TryGetValue(string.Empty, out PublishedCultureInfo? invariantInfos)
                ? invariantInfos.UrlSegment
                : null;
        }

        // handle context culture for variant
        if (culture == null)
        {
            culture = variationContextAccessor?.VariationContext?.Culture ?? string.Empty;
        }

        // get
        return culture != string.Empty && content.Cultures.TryGetValue(culture, out PublishedCultureInfo? infos)
            ? infos.UrlSegment
            : null;
    }

    #endregion

    #region Culture

    /// <summary>
    /// Determines whether the content has a culture.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="culture">The culture to check.</param>
    /// <remarks>Culture is case-insensitive.</remarks>
    public static bool HasCulture(this IPublishedContent content, string? culture)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        return content.Cultures.ContainsKey(culture ?? string.Empty);
    }

    /// <summary>
    /// Determines whether the content is invariant, or has a culture.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="culture">The culture to check.</param>
    /// <remarks>Culture is case-insensitive.</remarks>
    public static bool IsInvariantOrHasCulture(this IPublishedContent content, string culture)
        => !content.ContentType.VariesByCulture() || content.Cultures.ContainsKey(culture ?? string.Empty);

    /// <summary>
    /// Gets the culture date of the content item.
    /// </summary>
    /// <param name="content">The content item.</param>
    /// <param name="variationContextAccessor">The variation context accessor.</param>
    /// <param name="culture">
    /// The specific culture to get the date for. If null is used the current culture is used (Default is null).
    /// </param>
    public static DateTime CultureDate(this IPublishedContent content, IVariationContextAccessor variationContextAccessor, string? culture = null)
    {
        // invariant has invariant value (whatever the requested culture)
        if (!content.ContentType.VariesByCulture())
        {
            return content.UpdateDate;
        }

        // handle context culture for variant
        if (culture == null)
        {
            culture = variationContextAccessor?.VariationContext?.Culture ?? string.Empty;
        }

        // get
        return culture != string.Empty && content.Cultures.TryGetValue(culture, out PublishedCultureInfo? infos)
            ? infos.Date
            : DateTime.MinValue;
    }

    #endregion
}