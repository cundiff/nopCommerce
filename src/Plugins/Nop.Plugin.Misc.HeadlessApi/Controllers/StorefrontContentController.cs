using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Seo;
using Nop.Services.Topics;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

[Route(HeadlessApiDefaults.ApiRoutePrefix)]
public class StorefrontContentController : HeadlessApiControllerBase
{
    #region Fields

    protected readonly HeadlessApiModelFactory _modelFactory;
    protected readonly ICategoryService _categoryService;
    protected readonly IStoreContext _storeContext;
    protected readonly ITopicService _topicService;
    protected readonly IUrlRecordService _urlRecordService;

    #endregion

    #region Ctor

    public StorefrontContentController(ApiTokenService apiTokenService,
        HeadlessApiModelFactory modelFactory,
        ICategoryService categoryService,
        IStoreContext storeContext,
        ITopicService topicService,
        IUrlRecordService urlRecordService) : base(apiTokenService)
    {
        _modelFactory = modelFactory;
        _categoryService = categoryService;
        _storeContext = storeContext;
        _topicService = topicService;
        _urlRecordService = urlRecordService;
    }

    #endregion

    #region Menus

    /// <summary>
    /// getMenu(handle). The header menu is built from the top-level categories,
    /// the footer menu from the published topics. Any other handle falls back to categories.
    /// </summary>
    [HttpGet("menus/{handle}")]
    public virtual async Task<IActionResult> GetMenu(string handle)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var result = new List<MenuDto>();

        if (handle != null && handle.Contains("footer", StringComparison.OrdinalIgnoreCase))
        {
            var topics = await _topicService.GetAllTopicsAsync(store.Id);
            foreach (var topic in topics.Where(t => t.Published).OrderBy(t => t.DisplayOrder))
            {
                result.Add(new MenuDto
                {
                    Title = string.IsNullOrEmpty(topic.Title) ? topic.SystemName : topic.Title,
                    Path = $"/{topic.SystemName}"
                });
            }

            return JsonApi(result);
        }

        //header (and default): top-level categories
        var categories = await _categoryService.GetAllCategoriesByParentCategoryIdAsync(0);
        foreach (var category in categories)
        {
            var seName = await _urlRecordService.GetSeNameAsync(category);
            result.Add(new MenuDto { Title = category.Name, Path = $"/search/{seName}" });
        }

        return JsonApi(result);
    }

    #endregion

    #region Topics (CMS pages)

    /// <summary>
    /// getPages()
    /// </summary>
    [HttpGet("pages")]
    public virtual async Task<IActionResult> GetPages()
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var topics = await _topicService.GetAllTopicsAsync(store.Id);

        var result = topics
            .Where(t => t.Published)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => _modelFactory.PreparePageDto(t))
            .ToList();

        return JsonApi(result);
    }

    /// <summary>
    /// getPage(handle)
    /// </summary>
    [HttpGet("pages/{handle}")]
    public virtual async Task<IActionResult> GetPage(string handle)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var topic = await _topicService.GetTopicBySystemNameAsync(handle, store.Id);
        if (topic == null || !topic.Published)
            return NotFound();

        return JsonApi(_modelFactory.PreparePageDto(topic));
    }

    #endregion
}
