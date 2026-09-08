using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.ExternalAuth.Google.Components;

/// <summary>
/// Represents view component to display login button
/// </summary>
public class GoogleAuthenticationViewComponent : NopViewComponent
{
    /// <summary>
    /// Invoke view component
    /// </summary>
    /// <param name="widgetZone">Widget zone name</param>
    /// <param name="additionalData">Additional data</param>
    /// <returns>View component result</returns>
    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        return await ViewAsync("~/Plugins/ExternalAuth.Google/Views/PublicInfo.cshtml");
    }
}
