using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.HeadlessApi.Models;

/// <summary>
/// Represents the configuration model of the Headless Storefront API plugin
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.SecretKey")]
    public string SecretKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.AllowedOrigins")]
    public string AllowedOrigins { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.TokenExpirationDays")]
    public int TokenExpirationDays { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.EnableWebhooks")]
    public bool EnableWebhooks { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.RevalidationWebhookUrl")]
    public string RevalidationWebhookUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.RevalidationSecret")]
    public string RevalidationSecret { get; set; }

    public string ApiBaseUrl { get; set; }
}
