using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Http;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Factories;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Widgets.WishlistSharing.Controllers;

[WwwRequirement]
[CheckLanguageSeoCode]
[CheckAccessPublicStore]
[CheckAccessClosedStore]
[CheckDiscountCoupon]
[CheckAffiliate]
[AutoValidateAntiforgeryToken]
public class WishlistSharingController : BasePluginController
{
    #region Fields

    protected readonly ICustomWishlistService _customWishlistService;
    protected readonly ICustomerService _customerService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IPermissionService _permissionService;
    protected readonly IShoppingCartModelFactory _shoppingCartModelFactory;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWebHelper _webHelper;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public WishlistSharingController(ICustomWishlistService customWishlistService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        IPermissionService permissionService,
        IShoppingCartModelFactory shoppingCartModelFactory,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWebHelper webHelper,
        IWorkContext workContext)
    {
        _customWishlistService = customWishlistService;
        _customerService = customerService;
        _localizationService = localizationService;
        _permissionService = permissionService;
        _shoppingCartModelFactory = shoppingCartModelFactory;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _workContext = workContext;
    }

    #endregion

    #region Methods

    [HttpPost]
    public virtual async Task<IActionResult> Generate(int? wishlistId, int expirationDays)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_WISHLIST))
        {
            return Json(new
            {
                success = false,
                message = await _localizationService.GetResourceAsync("Plugins.Widgets.WishlistSharing.Error")
            });
        }

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, customWishlistId: wishlistId);
        if (!cart.Any())
        {
            return Json(new
            {
                success = false,
                message = await _localizationService.GetResourceAsync("Plugins.Widgets.WishlistSharing.EmptyWishlist")
            });
        }

        try
        {
            var wishlistShare = await _customWishlistService.GenerateWishlistShareAsync(customer.Id, wishlistId, expirationDays);
            var shareUrl = Url.RouteUrl(WishlistSharingDefaults.SharedWishlistRouteName,
                new { shareGuid = wishlistShare.ShareGuid },
                _webHelper.GetCurrentRequestProtocol());

            return Json(new
            {
                success = true,
                url = shareUrl,
                expiresOnUtc = wishlistShare.ExpiresOnUtc
            });
        }
        catch (ArgumentException)
        {
            return Json(new
            {
                success = false,
                message = await _localizationService.GetResourceAsync("Plugins.Widgets.WishlistSharing.InvalidExpiration")
            });
        }
        catch (InvalidOperationException)
        {
            return Json(new
            {
                success = false,
                message = await _localizationService.GetResourceAsync("Plugins.Widgets.WishlistSharing.Error")
            });
        }
    }

    [HttpGet]
    public virtual async Task<IActionResult> SharedWishlist(Guid shareGuid)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_WISHLIST))
            return RedirectToRoute(NopRouteNames.General.HOMEPAGE);

        var wishlistShare = await _customWishlistService.GetWishlistShareByGuidAsync(shareGuid);
        if (wishlistShare == null)
            return NotFound();

        var customer = await _customerService.GetCustomerByIdAsync(wishlistShare.CustomerId);
        if (customer == null)
            return NotFound();

        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, customWishlistId: wishlistShare.CustomWishlistId);

        var model = await _shoppingCartModelFactory.PrepareWishlistModelAsync(new WishlistModel(), cart, false, wishlistShare.CustomWishlistId);
        model.DisplayAddToCart = false;
        model.CustomerGuid = customer.CustomerGuid;
        model.CustomerFullname = await _customerService.GetCustomerFullNameAsync(customer);

        return View("~/Views/ShoppingCart/Wishlist.cshtml", model);
    }

    #endregion
}
