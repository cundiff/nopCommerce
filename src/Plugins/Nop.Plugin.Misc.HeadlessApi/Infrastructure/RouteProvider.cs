using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.HeadlessApi.Infrastructure;

/// <summary>
/// Registers the plugin routes
/// </summary>
public class RouteProvider : IRouteProvider
{
    /// <summary>
    /// Register routes
    /// </summary>
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        //admin configuration page
        endpointRouteBuilder.MapControllerRoute(name: HeadlessApiDefaults.ConfigurationRouteName,
            pattern: "Admin/HeadlessApi/Configure",
            defaults: new { controller = "HeadlessApi", action = "Configure", area = AreaNames.ADMIN });

        //enable attribute routing so the [Route]-decorated storefront API controllers are mapped
        endpointRouteBuilder.MapControllers();
    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 0;
}
