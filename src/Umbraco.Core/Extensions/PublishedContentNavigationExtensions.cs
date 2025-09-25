// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services.Navigation;

namespace Umbraco.Extensions.PublishedContent;

/// <summary>
/// Extension methods for IPublishedContent navigation and tree traversal operations.
/// </summary>
public static class PublishedContentNavigationExtensions
{
    #region Ancestors

    /// <summary>
    /// Gets the ancestors of the content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <returns>The ancestors of the content, in down-top order.</returns>
    /// <remarks>Does not consider the content itself.</remarks>
    public static IEnumerable<IPublishedContent> Ancestors(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService)
        => content.AncestorsOrSelf(navigationQueryService, publishedStatusFilteringService, false, null);

    /// <summary>
    /// Gets the ancestors of the content, at a level lesser or equal to a specified level.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="maxLevel">The level.</param>
    /// <returns>The ancestors of the content, at a level lesser or equal to the specified level, in down-top order.</returns>
    /// <remarks>Does not consider the content itself. Only content that are "high enough" in the tree are returned.</remarks>
    public static IEnumerable<IPublishedContent> Ancestors(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        int maxLevel)
        => content.AncestorsOrSelf(
            navigationQueryService,
            publishedStatusFilteringService,
            false,
            n => n.Level <= maxLevel);

    /// <summary>
    /// Gets the ancestors of the content, of a specified content type.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="contentTypeAlias">The content type alias.</param>
    /// <returns>The ancestors of the content, of the specified content type, in down-top order.</returns>
    /// <remarks>Does not consider the content itself. Returns all ancestors, of the specified content type.</remarks>
    public static IEnumerable<IPublishedContent> Ancestors(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        string contentTypeAlias)
        => content.AncestorsOrSelf(
            navigationQueryService,
            publishedStatusFilteringService,
            false,
            n => n.ContentType.Alias.InvariantEquals(contentTypeAlias));

    /// <summary>
    /// Gets the ancestors of the content, of a specified content type.
    /// </summary>
    /// <typeparam name="T">The content type.</typeparam>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <returns>The ancestors of the content, of the specified content type, in down-top order.</returns>
    /// <remarks>Does not consider the content itself. Returns all ancestors, of the specified content type.</remarks>
    public static IEnumerable<T> Ancestors<T>(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService)
        where T : class, IPublishedContent
        => content.AncestorsOrSelf(navigationQueryService, publishedStatusFilteringService, false, null).OfType<T>();

    #endregion

    #region AncestorsOrSelf

    /// <summary>
    /// Gets the ancestors of the content, and the content itself.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <returns>The ancestors of the content, and the content itself, in down-top order.</returns>
    public static IEnumerable<IPublishedContent> AncestorsOrSelf(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService)
        => content.AncestorsOrSelf(navigationQueryService, publishedStatusFilteringService, true, null);

    /// <summary>
    /// Gets the ancestors of the content or the content itself, at a level lesser or equal to a specified level.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="maxLevel">The level.</param>
    /// <returns>The ancestors of the content, and the content itself, at a level lesser or equal to the specified level,
    /// in down-top order.</returns>
    /// <remarks>Only content that are "high enough" in the tree are returned. So it may or may not return the content itself
    /// depending on its level.</remarks>
    public static IEnumerable<IPublishedContent> AncestorsOrSelf(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        int maxLevel)
        => content.AncestorsOrSelf(
            navigationQueryService,
            publishedStatusFilteringService,
            true,
            n => n.Level <= maxLevel);

    /// <summary>
    /// Gets the ancestors of the content or the content itself, of a specified content type.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="contentTypeAlias">The content type alias.</param>
    /// <returns>The ancestors of the content or the content itself, of the specified content type, in down-top order.</returns>
    /// <remarks>May or may not return the content itself depending on its content type.</remarks>
    public static IEnumerable<IPublishedContent> AncestorsOrSelf(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        string contentTypeAlias)
        => content.AncestorsOrSelf(
            navigationQueryService,
            publishedStatusFilteringService,
            true,
            n => n.ContentType.Alias.InvariantEquals(contentTypeAlias));

    /// <summary>
    /// Gets the ancestors of the content or the content itself, of a specified content type.
    /// </summary>
    /// <typeparam name="T">The content type.</typeparam>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <returns>The ancestors of the content or the content itself, of the specified content type, in down-top order.</returns>
    /// <remarks>May or may not return the content itself depending on its content type.</remarks>
    public static IEnumerable<T> AncestorsOrSelf<T>(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService)
        where T : class, IPublishedContent
        => content.AncestorsOrSelf(navigationQueryService, publishedStatusFilteringService, true, null).OfType<T>();

    #endregion

    #region Ancestor and AncestorOrSelf

    /// <summary>
    /// Gets the first ancestor of the content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <returns>The first ancestor of the content.</returns>
    /// <remarks>This method always returns the parent if it exists.</remarks>
    public static IPublishedContent? Ancestor(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService)
        => content.Ancestors(navigationQueryService, publishedStatusFilteringService).FirstOrDefault();

    /// <summary>
    /// Gets the first ancestor of the content, at a level lesser or equal to a specified level.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="maxLevel">The level.</param>
    /// <returns>The first ancestor of the content, at a level lesser or equal to the specified level.</returns>
    /// <remarks>Does not consider the content itself. Only content that are "high enough" in the tree are returned.</remarks>
    public static IPublishedContent? Ancestor(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        int maxLevel)
        => content.Ancestors(navigationQueryService, publishedStatusFilteringService, maxLevel).FirstOrDefault();

    /// <summary>
    /// Gets the first ancestor of the content, of a specified content type.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="contentTypeAlias">The content type alias.</param>
    /// <returns>The first ancestor of the content, of the specified content type.</returns>
    /// <remarks>Does not consider the content itself.</remarks>
    public static IPublishedContent? Ancestor(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        string contentTypeAlias)
        => content.Ancestors(navigationQueryService, publishedStatusFilteringService, contentTypeAlias).FirstOrDefault();

    /// <summary>
    /// Gets the first ancestor of the content, of a specified content type.
    /// </summary>
    /// <typeparam name="T">The content type.</typeparam>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <returns>The first ancestor of the content, of the specified content type.</returns>
    /// <remarks>Does not consider the content itself.</remarks>
    public static T? Ancestor<T>(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService)
        where T : class, IPublishedContent
        => content.Ancestors<T>(navigationQueryService, publishedStatusFilteringService).FirstOrDefault();

    /// <summary>
    /// Gets the content itself.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>Itself.</returns>
    public static IPublishedContent AncestorOrSelf(this IPublishedContent content) => content;

    /// <summary>
    /// Gets the first ancestor of the content or the content itself, at a specified level.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="maxLevel">The level.</param>
    /// <returns>The first ancestor of the content or the content itself, at the specified level.</returns>
    /// <remarks>May or may not return the content itself depending on its level.</remarks>
    public static IPublishedContent? AncestorOrSelf(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        int maxLevel)
        => content.AncestorsOrSelf(navigationQueryService, publishedStatusFilteringService, maxLevel).FirstOrDefault();

    /// <summary>
    /// Gets the first ancestor of the content or the content itself, of a specified content type.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <param name="contentTypeAlias">The content type alias.</param>
    /// <returns>The first ancestor of the content or the content itself, of the specified content type.</returns>
    /// <remarks>May or may not return the content itself depending on its content type.</remarks>
    public static IPublishedContent? AncestorOrSelf(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        string contentTypeAlias)
        => content.AncestorsOrSelf(navigationQueryService, publishedStatusFilteringService, contentTypeAlias).FirstOrDefault();

    /// <summary>
    /// Gets the first ancestor of the content or the content itself, of a specified content type.
    /// </summary>
    /// <typeparam name="T">The content type.</typeparam>
    /// <param name="content">The content.</param>
    /// <param name="navigationQueryService">The query service for the in-memory navigation structure.</param>
    /// <param name="publishedStatusFilteringService">The published status filtering service.</param>
    /// <returns>The first ancestor of the content or the content itself, of the specified content type.</returns>
    /// <remarks>May or may not return the content itself depending on its content type.</remarks>
    public static T? AncestorOrSelf<T>(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService)
        where T : class, IPublishedContent
        => content.AncestorsOrSelf<T>(navigationQueryService, publishedStatusFilteringService).FirstOrDefault();

    #endregion

    #region IsAncestor / IsDescendant

    /// <summary>
    /// Determines whether the content is a descendant of another content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="other">The other content.</param>
    /// <returns>True if the content is a descendant of the other content; otherwise false.</returns>
    public static bool IsDescendant(this IPublishedContent content, IPublishedContent other) =>
        other.Level < content.Level && content.Path.InvariantStartsWith(other.Path.EnsureEndsWith(','));

    /// <summary>
    /// Determines whether the content is a descendant of another content, or is the same as the other content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="other">The other content.</param>
    /// <returns>True if the content is a descendant of the other content, or is the same as the other content; otherwise false.</returns>
    public static bool IsDescendantOrSelf(this IPublishedContent content, IPublishedContent other) =>
        content.Path.InvariantEquals(other.Path) || content.IsDescendant(other);

    /// <summary>
    /// Determines whether the content is an ancestor of another content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="other">The other content.</param>
    /// <returns>True if the content is an ancestor of the other content; otherwise false.</returns>
    public static bool IsAncestor(this IPublishedContent content, IPublishedContent other) =>
        content.Level < other.Level && other.Path.InvariantStartsWith(content.Path.EnsureEndsWith(','));

    /// <summary>
    /// Determines whether the content is an ancestor of another content, or is the same as the other content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="other">The other content.</param>
    /// <returns>True if the content is an ancestor of the other content, or is the same as the other content; otherwise false.</returns>
    public static bool IsAncestorOrSelf(this IPublishedContent content, IPublishedContent other) =>
        other.Path.InvariantEquals(content.Path) || content.IsAncestor(other);

    #endregion

    #region Internal Helpers

    internal static IEnumerable<IPublishedContent> AncestorsOrSelf(
        this IPublishedContent content,
        INavigationQueryService navigationQueryService,
        IPublishedStatusFilteringService publishedStatusFilteringService,
        bool includeSelf,
        Func<IPublishedContent, bool>? predicate)
    {
        if (includeSelf && (predicate == null || predicate(content)))
            yield return content;

        IPublishedContent? parent = content.Parent;
        while (parent != null)
        {
            if (predicate == null || predicate(parent))
                yield return parent;
            parent = parent.Parent;
        }
    }

    #endregion
}