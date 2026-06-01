using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class HeadlessApiController : BasePluginController
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly IPermissionService _permissionService;
    protected readonly ISettingService _settingService;
    protected readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public HeadlessApiController(ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    #endregion

    #region Methods

    public async Task<IActionResult> Configure()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
            return AccessDeniedView();

        var settings = await _settingService.LoadSettingAsync<HeadlessApiSettings>();

        var model = new ConfigurationModel
        {
            SecretKey = settings.SecretKey,
            AllowedOrigins = settings.AllowedOrigins,
            TokenExpirationDays = settings.TokenExpirationDays,
            EnableWebhooks = settings.EnableWebhooks,
            RevalidationWebhookUrl = settings.RevalidationWebhookUrl,
            RevalidationSecret = settings.RevalidationSecret,
            ApiBaseUrl = $"{_webHelper.GetStoreLocation()}{HeadlessApiDefaults.ApiRoutePrefix}"
        };

        return View("~/Plugins/Misc.HeadlessApi/Views/Configure.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
            return AccessDeniedView();

        if (!ModelState.IsValid)
            return await Configure();

        var settings = await _settingService.LoadSettingAsync<HeadlessApiSettings>();

        settings.AllowedOrigins = model.AllowedOrigins;
        settings.TokenExpirationDays = model.TokenExpirationDays > 0 ? model.TokenExpirationDays : 30;
        settings.EnableWebhooks = model.EnableWebhooks;
        settings.RevalidationWebhookUrl = model.RevalidationWebhookUrl;
        settings.RevalidationSecret = model.RevalidationSecret;

        //allow regenerating the signing key by clearing the field
        if (!string.IsNullOrWhiteSpace(model.SecretKey))
            settings.SecretKey = model.SecretKey;
        else if (string.IsNullOrWhiteSpace(settings.SecretKey))
            settings.SecretKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        await _settingService.SaveSettingAsync(settings);
        await _settingService.ClearCacheAsync();

        _notificationService.SuccessNotification(
            await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    #endregion
}
