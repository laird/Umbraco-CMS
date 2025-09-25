// Copyright (c) Umbraco.
// See LICENSE for more details.

using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Extensions.PublishedContent;

/// <summary>
/// Extension methods for IPublishedContent template operations.
/// </summary>
public static class PublishedContentTemplateExtensions
{
    /// <summary>
    /// Returns the current template alias.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="fileService">The file service.</param>
    /// <returns>The template alias, or empty string if none is set.</returns>
    public static string GetTemplateAlias(this IPublishedContent content, IFileService fileService)
    {
        if (content.TemplateId.HasValue == false)
        {
            return string.Empty;
        }

        ITemplate? template = fileService.GetTemplate(content.TemplateId.Value);
        return template?.Alias ?? string.Empty;
    }

    /// <summary>
    /// Determines whether a template is allowed for the content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="contentTypeService">The content type service.</param>
    /// <param name="webRoutingSettings">The web routing settings.</param>
    /// <param name="templateId">The template ID to check.</param>
    /// <returns>True if the template is allowed; otherwise false.</returns>
    public static bool IsAllowedTemplate(
        this IPublishedContent content, 
        IContentTypeService contentTypeService, 
        WebRoutingSettings webRoutingSettings, 
        int templateId) =>
        content.IsAllowedTemplate(
            contentTypeService, 
            webRoutingSettings.DisableAlternativeTemplates, 
            webRoutingSettings.ValidateAlternativeTemplates, 
            templateId);

    /// <summary>
    /// Determines whether a template is allowed for the content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="contentTypeService">The content type service.</param>
    /// <param name="disableAlternativeTemplates">Whether alternative templates are disabled.</param>
    /// <param name="validateAlternativeTemplates">Whether to validate alternative templates.</param>
    /// <param name="templateId">The template ID to check.</param>
    /// <returns>True if the template is allowed; otherwise false.</returns>
    public static bool IsAllowedTemplate(
        this IPublishedContent content, 
        IContentTypeService contentTypeService, 
        bool disableAlternativeTemplates, 
        bool validateAlternativeTemplates, 
        int templateId)
    {
        if (disableAlternativeTemplates)
        {
            return content.TemplateId == templateId;
        }

        if (content.TemplateId == templateId || !validateAlternativeTemplates)
        {
            return true;
        }

        IContentType? publishedContentContentType = contentTypeService.Get(content.ContentType.Id);
        if (publishedContentContentType == null)
        {
            throw new NullReferenceException($"No content type returned for published content (contentType='{content.ContentType.Id}')");
        }

        return publishedContentContentType.IsAllowedTemplate(templateId);
    }

    /// <summary>
    /// Determines whether a template is allowed for the content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="fileService">The file service.</param>
    /// <param name="contentTypeService">The content type service.</param>
    /// <param name="disableAlternativeTemplates">Whether alternative templates are disabled.</param>
    /// <param name="validateAlternativeTemplates">Whether to validate alternative templates.</param>
    /// <param name="templateAlias">The template alias to check.</param>
    /// <returns>True if the template is allowed; otherwise false.</returns>
    public static bool IsAllowedTemplate(
        this IPublishedContent content, 
        IFileService fileService, 
        IContentTypeService contentTypeService, 
        bool disableAlternativeTemplates, 
        bool validateAlternativeTemplates, 
        string templateAlias)
    {
        ITemplate? template = fileService.GetTemplate(templateAlias);
        return template != null && content.IsAllowedTemplate(
            contentTypeService, 
            disableAlternativeTemplates, 
            validateAlternativeTemplates, 
            template.Id);
    }
}