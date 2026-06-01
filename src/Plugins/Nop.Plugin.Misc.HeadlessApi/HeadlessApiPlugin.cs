using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.HeadlessApi;

/// <summary>
/// Represents the Headless Storefront API plugin
/// </summary>
public class HeadlessApiPlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly ISettingService _settingService;
    protected readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public HeadlessApiPlugin(ILocalizationService localizationService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/HeadlessApi/Configure";
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        //default settings
        await _settingService.SaveSettingAsync(new HeadlessApiSettings
        {
            SecretKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            AllowedOrigins = "*",
            TokenExpirationDays = 30,
            EnableWebhooks = false,
            RevalidationWebhookUrl = string.Empty,
            RevalidationSecret = Guid.NewGuid().ToString("N")
        });

        //locale resources
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.HeadlessApi.Fields.SecretKey"] = "Token signing secret",
            ["Plugins.Misc.HeadlessApi.Fields.AllowedOrigins"] = "Allowed origins (CORS)",
            ["Plugins.Misc.HeadlessApi.Fields.TokenExpirationDays"] = "Token expiration (days)",
            ["Plugins.Misc.HeadlessApi.Fields.EnableWebhooks"] = "Enable revalidation webhooks",
            ["Plugins.Misc.HeadlessApi.Fields.RevalidationWebhookUrl"] = "Revalidation webhook URL",
            ["Plugins.Misc.HeadlessApi.Fields.RevalidationSecret"] = "Revalidation secret"
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        //settings
        await _settingService.DeleteSettingAsync<HeadlessApiSettings>();

        //locale resources
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.HeadlessApi");

        await base.UninstallAsync();
    }

    #endregion
}
