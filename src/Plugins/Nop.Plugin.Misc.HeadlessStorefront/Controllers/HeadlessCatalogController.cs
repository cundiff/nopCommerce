using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.HeadlessStorefront.Services;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HeadlessStorefront.Controllers;

[ApiController]
[IgnoreAntiforgeryToken]
[Route($"{HeadlessStorefrontDefaults.ApiRoutePrefix}")]
public class HeadlessCatalogController : HeadlessApiControllerBase
{
  private readonly IHeadlessCatalogService _catalogService;

  public HeadlessCatalogController(
    IHeadlessSessionService sessionService,
    IWorkContext workContext,
    IHeadlessCatalogService catalogService)
    : base(sessionService, workContext)
  {
    _catalogService = catalogService;
  }

  [HttpGet("health")]
  public IActionResult Health()
  {
    return Ok(new { status = "ok" });
  }

  [HttpGet("categories")]
  public async Task<IActionResult> GetCategories()
  {
    var categories = await _catalogService.GetCollectionsAsync();
    return Ok(categories);
  }

  [HttpGet("categories/{seName}")]
  public async Task<IActionResult> GetCategory(string seName)
  {
    var category = await _catalogService.GetCollectionByHandleAsync(seName);
    return category == null ? NotFound() : Ok(category);
  }

  [HttpGet("categories/{seName}/products")]
  public async Task<IActionResult> GetCategoryProducts(string seName, [FromQuery] string sortKey = null, [FromQuery] bool reverse = false)
  {
    var categoryId = await _catalogService.ResolveCategoryIdByHandleAsync(seName);
    if (categoryId == null)
      return NotFound();

    var products = await _catalogService.GetProductsAsync(null, sortKey, reverse, categoryId);
    return Ok(products);
  }

  [HttpGet("products")]
  public async Task<IActionResult> GetProducts([FromQuery] string q = null, [FromQuery] string sortKey = null, [FromQuery] bool reverse = false)
  {
    var products = await _catalogService.GetProductsAsync(q, sortKey, reverse);
    return Ok(products);
  }

  [HttpGet("products/{seName}")]
  public async Task<IActionResult> GetProduct(string seName)
  {
    var product = await _catalogService.GetProductByHandleAsync(seName);
    return product == null ? NotFound() : Ok(product);
  }
}
