using Microsoft.AspNetCore.Mvc;
using Nop.Core;
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

    private async Task<string> ResolveAttributesXmlAsync(int productId, int? combinationId)
    {
        if (!combinationId.HasValue)
            return string.Empty;

        var combination = await _productAttributeService.GetProductAttributeCombinationByIdAsync(combinationId.Value);
        if (combination == null || combination.ProductId != productId)
            return string.Empty;

        return combination.AttributesXml;
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

        var store = await _storeContext.GetCurrentStoreAsync();

        foreach (var line in request?.Lines ?? new List<AddToCartLineRequest>())
        {
            if (!HeadlessApiModelFactory.TryParseVariantId(line.MerchandiseId, out var productId, out var combinationId))
                continue;

            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null || product.Deleted || !product.Published)
                continue;

            var attributesXml = await ResolveAttributesXmlAsync(productId, combinationId);
            var quantity = line.Quantity > 0 ? line.Quantity : 1;

            await _shoppingCartService.AddToCartAsync(customer, product,
                ShoppingCartType.ShoppingCart, store.Id, attributesXml, quantity: quantity);
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
