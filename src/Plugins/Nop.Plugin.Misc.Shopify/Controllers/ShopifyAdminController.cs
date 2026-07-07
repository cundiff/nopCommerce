using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.Shopify.Models;
using Nop.Plugin.Misc.Shopify.Services;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.ScheduleTasks;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.Shopify.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class ShopifyAdminController : BasePluginController
{
    #region Fields

    protected readonly IDateTimeHelper _dateTimeHelper;
    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly IScheduleTaskService _scheduleTaskService;
    protected readonly ISettingService _settingService;
    protected readonly ShopifyRecordService _shopifyRecordService;
    protected readonly ShopifyService _shopifyService;
    protected readonly ShopifySettings _shopifySettings;

    #endregion

    #region Ctor

    public ShopifyAdminController(IDateTimeHelper dateTimeHelper,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService,
        ShopifyRecordService shopifyRecordService,
        ShopifyService shopifyService,
        ShopifySettings shopifySettings)
    {
        _dateTimeHelper = dateTimeHelper;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
        _shopifyRecordService = shopifyRecordService;
        _shopifyService = shopifyService;
        _shopifySettings = shopifySettings;
    }

    #endregion

    #region Utilities

    protected async Task<ConfigurationModel> PrepareConfigurationModelAsync()
    {
        var model = new ConfigurationModel
        {
            ShopDomain = _shopifySettings.ShopDomain,
            AccessToken = _shopifySettings.AccessToken,
            SyncEnabled = _shopifySettings.SyncEnabled,
            AutoSyncEnabled = _shopifySettings.AutoSyncEnabled,
            AutoSyncPeriod = _shopifySettings.AutoSyncPeriod,
            PriceSyncEnabled = _shopifySettings.PriceSyncEnabled,
            ImageSyncEnabled = _shopifySettings.ImageSyncEnabled,
            InventorySyncEnabled = _shopifySettings.InventorySyncEnabled,
            AutoAddRecordsEnabled = _shopifySettings.AutoAddRecordsEnabled,
            ShopName = _shopifySettings.ShopName,
            LastSyncError = _shopifySettings.LastSyncError,
            PendingRecords = await _shopifyRecordService.GetPendingRecordsCountAsync(),
            IsConfigured = ShopifyService.IsConfigured(_shopifySettings)
        };

        if (_shopifySettings.LastSyncUtc.HasValue)
        {
            model.LastSyncUtc = (await _dateTimeHelper
                .ConvertToUserTimeAsync(_shopifySettings.LastSyncUtc.Value, DateTimeKind.Utc)).ToString("g");
        }

        return model;
    }

    protected async Task SaveSettingsAsync(ConfigurationModel model)
    {
        _shopifySettings.ShopDomain = model.ShopDomain?.Trim();
        _shopifySettings.AccessToken = model.AccessToken?.Trim();
        _shopifySettings.SyncEnabled = model.SyncEnabled;
        _shopifySettings.AutoSyncEnabled = model.AutoSyncEnabled;
        _shopifySettings.AutoSyncPeriod = model.AutoSyncPeriod;
        _shopifySettings.PriceSyncEnabled = model.PriceSyncEnabled;
        _shopifySettings.ImageSyncEnabled = model.ImageSyncEnabled;
        _shopifySettings.InventorySyncEnabled = model.InventorySyncEnabled;
        _shopifySettings.AutoAddRecordsEnabled = model.AutoAddRecordsEnabled;

        await _settingService.SaveSettingAsync(_shopifySettings);

        var scheduleTask = await _scheduleTaskService.GetTaskByTypeAsync(ShopifyDefaults.SynchronizationTask.Type);
        if (scheduleTask is not null)
        {
            scheduleTask.Enabled = model.AutoSyncEnabled;
            scheduleTask.Seconds = model.AutoSyncPeriod * 60;
            await _scheduleTaskService.UpdateTaskAsync(scheduleTask);
        }
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure()
    {
        var model = await PrepareConfigurationModelAsync();
        return View("~/Plugins/Misc.Shopify/Views/Configure.cshtml", model);
    }

    [HttpPost, ActionName("Configure")]
    [FormValueRequired("save")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Save(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        await SaveSettingsAsync(model);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    [HttpPost, ActionName("Configure")]
    [FormValueRequired("test-connection")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> TestConnection(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        await SaveSettingsAsync(model);

        var (success, message) = await _shopifyService.TestConnectionAsync();
        if (success)
            _notificationService.SuccessNotification(message);
        else
            _notificationService.ErrorNotification(string.Format(await _localizationService.GetResourceAsync("Plugins.Misc.Shopify.ConnectionFailed"), message));

        return await Configure();
    }

    [HttpPost, ActionName("Configure")]
    [FormValueRequired("sync-now")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> SyncNow(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        await SaveSettingsAsync(model);

        if (!ShopifyService.IsConfigured(_shopifySettings))
        {
            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.Shopify.Fields.NotConfigured"));
            return await Configure();
        }

        try
        {
            await _shopifyService.SyncAsync();
            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.Shopify.SyncStarted"));
        }
        catch (Exception exception)
        {
            _notificationService.ErrorNotification(exception.Message);
        }

        return await Configure();
    }

    #endregion
}
