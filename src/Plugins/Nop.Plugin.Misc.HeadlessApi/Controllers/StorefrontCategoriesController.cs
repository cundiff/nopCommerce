using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

[Route(HeadlessApiDefaults.ApiRoutePrefix + "/categories")]
public class StorefrontCategoriesController : HeadlessApiControllerBase
{
    #region Fields

    protected readonly HeadlessApiModelFactory _modelFactory;
    protected readonly ICategoryService _categoryService;
    protected readonly IProductService _productService;
    protected readonly IStoreContext _storeContext;
    protected readonly IUrlRecordService _urlRecordService;

    #endregion

    #region Ctor

    public StorefrontCategoriesController(ApiTokenService apiTokenService,
        HeadlessApiModelFactory modelFactory,
        ICategoryService categoryService,
        IProductService productService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService) : base(apiTokenService)
    {
        _modelFactory = modelFactory;
        _categoryService = categoryService;
        _productService = productService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
    }

    #endregion

    #region Utilities

    private static ProductSortingEnum MapSortKey(string sortKey, bool reverse)
    {
        return (sortKey?.ToUpperInvariant()) switch
        {
            "PRICE" => reverse ? ProductSortingEnum.PriceDesc : ProductSortingEnum.PriceAsc,
            "CREATED_AT" => ProductSortingEnum.CreatedOn,
            "BEST_SELLING" => ProductSortingEnum.Position,
            _ => ProductSortingEnum.Position
        };
    }

    private async Task<Category> GetCategoryByHandleAsync(string handle)
    {
        var urlRecord = await _urlRecordService.GetBySlugAsync(handle);
        if (urlRecord == null || !urlRecord.IsActive ||
            !urlRecord.EntityName.Equals("Category", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var category = await _categoryService.GetCategoryByIdAsync(urlRecord.EntityId);
        if (category == null || category.Deleted || !category.Published)
            return null;

        return category;
    }

    #endregion

    #region Methods

    /// <summary>
    /// getCollections()
    /// </summary>
    [HttpGet]
    public virtual async Task<IActionResult> GetCollections()
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var categories = await _categoryService.GetAllCategoriesAsync(store.Id);

        var result = new List<CollectionDto>();
        foreach (var category in categories)
            result.Add(await _modelFactory.PrepareCollectionDtoAsync(category));

        return JsonApi(result);
    }

    /// <summary>
    /// getCollection(handle)
    /// </summary>
    [HttpGet("by-handle/{handle}")]
    public virtual async Task<IActionResult> GetCollection(string handle)
    {
        var category = await GetCategoryByHandleAsync(handle);
        if (category == null)
            return NotFound();

        return JsonApi(await _modelFactory.PrepareCollectionDtoAsync(category));
    }

    /// <summary>
    /// getCollectionProducts({ collection, reverse, sortKey })
    /// </summary>
    [HttpGet("by-handle/{handle}/products")]
    public virtual async Task<IActionResult> GetCollectionProducts(string handle, string sort, bool reverse = false)
    {
        var category = await GetCategoryByHandleAsync(handle);
        if (category == null)
            return JsonApi(new List<ProductDto>());

        var store = await _storeContext.GetCurrentStoreAsync();
        var orderBy = MapSortKey(sort, reverse);

        var products = await _productService.SearchProductsAsync(
            storeId: store.Id,
            categoryIds: new List<int> { category.Id },
            visibleIndividuallyOnly: true,
            orderBy: orderBy);

        var result = new List<ProductDto>();
        foreach (var product in products)
            result.Add(await _modelFactory.PrepareProductDtoAsync(product));

        return JsonApi(result);
    }

    #endregion
}
