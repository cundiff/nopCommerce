using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Widgets.WishlistSharing.Models;
using Nop.Services.Orders;
using Nop.Web.Framework.Components;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Widgets.WishlistSharing.Components;

/// <summary>
/// Represents wishlist sharing widget view component
/// </summary>
public class WishlistSharingViewComponent : NopViewComponent
{
    #region Methods

    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (additionalData is not WishlistModel wishlistModel ||
            !wishlistModel.IsEditable ||
            !wishlistModel.Items.Any())
        {
            return Content("");
        }

        var model = new PublicInfoModel
        {
            ListId = wishlistModel.ListId,
            ExpirationDays = NopOrderDefaults.WishlistShareExpirationDays.ToList()
        };

        return await ViewAsync("~/Plugins/Widgets.WishlistSharing/Views/PublicInfo.cshtml", model);
    }

    #endregion
}
