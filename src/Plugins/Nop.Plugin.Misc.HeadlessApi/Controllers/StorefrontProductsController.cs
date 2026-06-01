using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

[Route(HeadlessApiDefaults.ApiRoutePrefix + "/products")]
public class StorefrontProductsController : HeadlessApiControllerBase
{
    #region Fields

    protected readonly HeadlessApiModelFactory _modelFactory;
    protected readonly IProductService _productService;
    protected readonly IStoreContext _storeContext;
    protected readonly IUrlRecordService _urlRecordService;

    #endregion

    #region Ctor

    public StorefrontProductsController(ApiTokenService apiTokenService,
        HeadlessApiModelFactory modelFactory,
        IProductService productService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService) : base(apiTokenService)
    {
        _modelFactory = modelFactory;
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

    #endregion

    #region Methods

    /// <summary>
    /// getProducts({ query, reverse, sortKey })
    /// </summary>
    [HttpGet]
    public virtual async Task<IActionResult> GetProducts(string q, string sort, bool reverse = false)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var orderBy = MapSortKey(sort, reverse);

        var products = await _productService.SearchProductsAsync(
            storeId: store.Id,
            visibleIndividuallyOnly: true,
            keywords: string.IsNullOrWhiteSpace(q) ? null : q,
            orderBy: orderBy);

        var result = new List<ProductDto>();
        foreach (var product in products)
            result.Add(await _modelFactory.PrepareProductDtoAsync(product));

        return JsonApi(result);
    }

    /// <summary>
    /// Products displayed on the home page (used by the storefront carousel / featured sections)
    /// </summary>
    [HttpGet("homepage")]
    public virtual async Task<IActionResult> GetHomepageProducts()
    {
        var products = await _productService.GetAllProductsDisplayedOnHomepageAsync();

        var result = new List<ProductDto>();
        foreach (var product in products.Where(p => p.Published && !p.Deleted))
            result.Add(await _modelFactory.PrepareProductDtoAsync(product));

        return JsonApi(result);
    }

    /// <summary>
    /// getProduct(handle)
    /// </summary>
    [HttpGet("by-handle/{handle}")]
    public virtual async Task<IActionResult> GetProductByHandle(string handle)
    {
        var urlRecord = await _urlRecordService.GetBySlugAsync(handle);
        if (urlRecord == null || !urlRecord.IsActive ||
            !urlRecord.EntityName.Equals("Product", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var product = await _productService.GetProductByIdAsync(urlRecord.EntityId);
        if (product == null || product.Deleted || !product.Published)
            return NotFound();

        return JsonApi(await _modelFactory.PrepareProductDtoAsync(product));
    }

    /// <summary>
    /// getProductRecommendations(productId)
    /// </summary>
    [HttpGet("{id:int}/recommendations")]
    public virtual async Task<IActionResult> GetProductRecommendations(int id)
    {
        var related = await _productService.GetRelatedProductsByProductId1Async(id);
        var relatedIds = related.Select(rp => rp.ProductId2).ToArray();
        var products = await _productService.GetProductsByIdsAsync(relatedIds);

        var result = new List<ProductDto>();
        foreach (var product in products.Where(p => p.Published && !p.Deleted))
            result.Add(await _modelFactory.PrepareProductDtoAsync(product));

        return JsonApi(result);
    }

    #endregion
}
