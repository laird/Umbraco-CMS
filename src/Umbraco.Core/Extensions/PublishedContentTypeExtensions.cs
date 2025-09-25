// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Extensions.PublishedContent;

/// <summary>
/// Extension methods for IPublishedContent type checking and comparison operations.
/// </summary>
public static class PublishedContentTypeExtensions
{
    #region IsComposedOf

    /// <summary>
    /// Gets a value indicating whether the content is of a content type composed of the given alias.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="alias">The content type alias.</param>
    /// <returns>True if the content is of a content type composed of a content type identified by the alias; otherwise false.</returns>
    public static bool IsComposedOf(this IPublishedContent content, string alias) =>
        content.ContentType.CompositionAliases.InvariantContains(alias);

    #endregion

    #region IsDocumentType

    /// <summary>
    /// Determines if the current content is of a specific content type.
    /// </summary>
    /// <param name="content">The content to check.</param>
    /// <param name="docTypeAlias">The alias of the content type to check against.</param>
    /// <returns>True if the content is of the specified type; otherwise false.</returns>
    public static bool IsDocumentType(this IPublishedContent content, string docTypeAlias) =>
        content.ContentType.Alias.InvariantEquals(docTypeAlias);

    /// <summary>
    /// Determines if the current content is of a specific content type or its compositions.
    /// </summary>
    /// <param name="content">The content to check.</param>
    /// <param name="docTypeAlias">The alias of the content type to check against.</param>
    /// <param name="recursive">If true, also checks compositions; if false, only checks the direct type.</param>
    /// <returns>True if the content is of the specified type (or its compositions if recursive); otherwise false.</returns>
    [Obsolete("The recursive parameter is misleading, consider using the overload IsDocumentType(this IPublishedContent content, string docTypeAlias).")]
    public static bool IsDocumentType(this IPublishedContent content, string docTypeAlias, bool recursive)
    {
        if (content.ContentType.Alias.InvariantEquals(docTypeAlias))
        {
            return true;
        }

        return recursive && content.IsComposedOf(docTypeAlias);
    }

    #endregion

    #region Equality

    /// <summary>
    /// Determines whether this content is equal to another content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="other">The other content.</param>
    /// <returns>True if the contents are equal; otherwise false.</returns>
    public static bool IsEqual(this IPublishedContent content, IPublishedContent other) => 
        content.Id == other.Id;

    /// <summary>
    /// Determines whether this content is not equal to another content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="other">The other content.</param>
    /// <returns>True if the contents are not equal; otherwise false.</returns>
    public static bool IsNotEqual(this IPublishedContent content, IPublishedContent other) =>
        content.IsEqual(other) == false;

    #endregion
}