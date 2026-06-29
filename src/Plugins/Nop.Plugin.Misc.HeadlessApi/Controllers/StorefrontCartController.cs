using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

[Route(HeadlessApiDefaults.ApiRoutePrefix + "/cart")]
public class StorefrontCartController : HeadlessApiControllerBase
{
    #region Fields

    protected readonly HeadlessApiModelFactory _modelFactory;
    protected readonly ICustomerService _customerService;
    protected readonly IProductAttributeService _productAttributeService;
    protected readonly IProductService _productService;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStoreContext _storeContext;

    #endregion

    #region Ctor

    public StorefrontCartController(ApiTokenService apiTokenService,
        HeadlessApiModelFactory modelFactory,
        ICustomerService customerService,
        IProductAttributeService productAttributeService,
        IProductService productService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext) : base(apiTokenService)
    {
        _modelFactory = modelFactory;
        _customerService = customerService;
        _productAttributeService = productAttributeService;
        _productService = productService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
    }

    #endregion

    #region Utilities

    private async Task<(bool isValid, string attributesXml, string error)> ResolveAttributesXmlAsync(Product product, int? combinationId)
    {
        if (!combinationId.HasValue)
            return (true, string.Empty, null);

        var combination = await _productAttributeService.GetProductAttributeCombinationByIdAsync(combinationId.Value);
        if (combination == null || combination.ProductId != product.Id)
            return (false, string.Empty, "Variant is invalid for this product.");

        var (availableForSale, availabilityStatus) = HeadlessApiModelFactory.GetAvailabilityForCombination(
            product,
            combination);
        if (!availableForSale)
            return (false, string.Empty, $"Variant is {availabilityStatus}.");

        return (true, combination.AttributesXml, null);
    }

    private IActionResult BuildBadRequest(string merchandiseId, string error, int lineIndex)
    {
        return BadRequest(new
        {
            error,
            merchandiseId,
            line = lineIndex
        });
    }

    #endregion

    #region Methods

    /// <summary>
    /// createCart(): creates a guest customer and returns an empty cart whose id is the session token
    /// </summary>
    [HttpPost]
    public virtual async Task<IActionResult> CreateCart()
    {
        var customer = await _customerService.InsertGuestCustomerAsync();
        var token = _apiTokenService.GenerateToken(customer.CustomerGuid);

        return JsonApi(await _modelFactory.PrepareCartDtoAsync(customer, token));
    }

    /// <summary>
    /// getCart()
    /// </summary>
    [HttpGet]
    public virtual async Task<IActionResult> GetCart()
    {
        var token = GetRequestToken();
        var customer = await _apiTokenService.GetCustomerFromTokenAsync(token);
        if (customer == null)
            return NotFound();

        return JsonApi(await _modelFactory.PrepareCartDtoAsync(customer, token));
    }

    /// <summary>
    /// addToCart(lines)
    /// </summary>
    [HttpPost("items")]
    public virtual async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
    {
        var token = GetRequestToken();
        var customer = await _apiTokenService.GetCustomerFromTokenAsync(token);
        if (customer == null)
            return Unauthorized();

        if (request?.Lines == null || !request.Lines.Any())
            return BadRequest(new { error = "At least one cart line is required." });

        var store = await _storeContext.GetCurrentStoreAsync();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (!HeadlessApiModelFactory.TryParseVariantId(line.MerchandiseId, out var productId, out var combinationId))
                return BuildBadRequest(line.MerchandiseId, "Merchandise identifier is invalid.", index);

            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null || product.Deleted || !product.Published)
                return BuildBadRequest(line.MerchandiseId, "Product was not found.", index);

            if (!combinationId.HasValue)
            {
                var (productAvailable, availabilityStatus) = HeadlessApiModelFactory.GetAvailabilityForProduct(product);
                if (!productAvailable)
                    return BuildBadRequest(line.MerchandiseId, $"Product is {availabilityStatus}.", index);
            }

            var (attributesValid, attributesXml, attributesError) = await ResolveAttributesXmlAsync(product, combinationId);
            if (!attributesValid)
                return BuildBadRequest(line.MerchandiseId, attributesError, index);

            var quantity = line.Quantity > 0 ? line.Quantity : 1;
            var warnings = await _shoppingCartService.AddToCartAsync(customer, product,
                ShoppingCartType.ShoppingCart, store.Id, attributesXml, quantity: quantity);

            if (warnings.Any())
                return BuildBadRequest(line.MerchandiseId, string.Join(" ", warnings), index);
        }

        return JsonApi(await _modelFactory.PrepareCartDtoAsync(customer, token));
    }

    /// <summary>
    /// updateCart(lines)
    /// </summary>
    [HttpPut("items")]
    public virtual async Task<IActionResult> UpdateCart([FromBody] UpdateCartRequest request)
    {
        var token = GetRequestToken();
        var customer = await _apiTokenService.GetCustomerFromTokenAsync(token);
        if (customer == null)
            return Unauthorized();

        var store = await _storeContext.GetCurrentStoreAsync();
        var cartItems = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        foreach (var line in request?.Lines ?? new List<UpdateCartLineRequest>())
        {
            if (!int.TryParse(line.Id, out var cartItemId))
                continue;

            var item = cartItems.FirstOrDefault(ci => ci.Id == cartItemId);
            if (item == null)
                continue;

            if (line.Quantity <= 0)
            {
                await _shoppingCartService.DeleteShoppingCartItemAsync(item);
                continue;
            }

            await _shoppingCartService.UpdateShoppingCartItemAsync(customer, item.Id,
                item.AttributesXml, item.CustomerEnteredPrice, quantity: line.Quantity);
        }

        return JsonApi(await _modelFactory.PrepareCartDtoAsync(customer, token));
    }

    /// <summary>
    /// removeFromCart(lineIds)
    /// </summary>
    [HttpDelete("items")]
    public virtual async Task<IActionResult> RemoveFromCart([FromBody] RemoveFromCartRequest request)
    {
        var token = GetRequestToken();
        var customer = await _apiTokenService.GetCustomerFromTokenAsync(token);
        if (customer == null)
            return Unauthorized();

        var store = await _storeContext.GetCurrentStoreAsync();
        var cartItems = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        foreach (var lineId in request?.LineIds ?? new List<string>())
        {
            if (!int.TryParse(lineId, out var cartItemId))
                continue;

            var item = cartItems.FirstOrDefault(ci => ci.Id == cartItemId);
            if (item != null)
                await _shoppingCartService.DeleteShoppingCartItemAsync(item);
        }

        return JsonApi(await _modelFactory.PrepareCartDtoAsync(customer, token));
    }

    #endregion
}
