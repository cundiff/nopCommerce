using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;

namespace Nop.Plugin.Widgets.WishlistSharing.Infrastructure;

/// <summary>
/// Represents plugin route provider
/// </summary>
public class RouteProvider : BaseRouteProvider, IRouteProvider
{
    /// <summary>
    /// Register routes
    /// </summary>
    /// <param name="endpointRouteBuilder">Route builder</param>
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        var lang = GetLanguageRoutePattern();

        endpointRouteBuilder.MapControllerRoute(name: WishlistSharingDefaults.SharedWishlistRouteName,
            pattern: $"{lang}/wishlist/shared/{{shareGuid:guid}}",
            defaults: new { controller = "WishlistSharing", action = "SharedWishlist" });

        endpointRouteBuilder.MapControllerRoute(name: WishlistSharingDefaults.GenerateShareRouteName,
            pattern: "wishlist/share/generate",
            defaults: new { controller = "WishlistSharing", action = "Generate" });
    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 10;
}
