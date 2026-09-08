using Nop.Plugin.ExternalAuth.Google.Components;
using Nop.Services.Authentication.External;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.ExternalAuth.Google;

/// <summary>
/// Represents method for the authentication with Google account
/// </summary>
public class GoogleAuthenticationMethod : BasePlugin, IExternalAuthenticationMethod
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly ISettingService _settingService;
    protected readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public GoogleAuthenticationMethod(ILocalizationService localizationService,
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
        return $"{_webHelper.GetStoreLocation()}Admin/GoogleAuthentication/Configure";
    }

    /// <summary>
    /// Gets a type of a view component for displaying plugin in public store
    /// </summary>
    /// <returns>View component type</returns>
    public Type GetPublicViewComponent()
    {
        return typeof(GoogleAuthenticationViewComponent);
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new GoogleExternalAuthSettings());

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.ExternalAuth.Google.ClientId"] = "Client ID",
            ["Plugins.ExternalAuth.Google.ClientId.Hint"] = "Enter your Google OAuth client ID. You can find it in the Google Cloud Console credentials page.",
            ["Plugins.ExternalAuth.Google.ClientSecret"] = "Client secret",
            ["Plugins.ExternalAuth.Google.ClientSecret.Hint"] = "Enter your Google OAuth client secret. You can find it in the Google Cloud Console credentials page.",
            ["Plugins.ExternalAuth.Google.Instructions"] = "<p>To configure authentication with Google, please follow these steps:<br/><br/><ol><li>Go to the <a href=\"https://console.cloud.google.com/apis/credentials\" target=\"_blank\">Google Cloud Console credentials</a> page and sign in.</li><li>Create or select a project, then click <b>Create credentials</b> and choose <b>OAuth client ID</b>.</li><li>If prompted, configure the OAuth consent screen for an external user type.</li><li>Set the application type to <b>Web application</b>.</li><li>Add \"{0:s}signin-google\" to the <b>Authorized redirect URIs</b> field.</li><li>Click <b>Create</b>, then copy the Client ID and Client secret below.</li></ol><br/><br/></p>"
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<GoogleExternalAuthSettings>();

        await _localizationService.DeleteLocaleResourcesAsync("Plugins.ExternalAuth.Google");

        await base.UninstallAsync();
    }

    #endregion
}
