using Nop.Core.Domain.Media;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.Shopify;

/// <summary>
/// Represents Shopify plugin
/// </summary>
public class ShopifyPlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly INopUrlHelper _nopUrlHelper;
    protected readonly IScheduleTaskService _scheduleTaskService;
    protected readonly ISettingService _settingService;

    #endregion

    #region Ctor

    public ShopifyPlugin(ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService)
    {
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(ShopifyDefaults.ConfigurationRouteName);
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    public override async Task InstallAsync()
    {
        await _settingService.SetSettingAsync($"{nameof(MediaSettings)}.{nameof(MediaSettings.UseAbsoluteImagePath)}", true, clearCache: false);

        await _settingService.SaveSettingAsync(new ShopifySettings
        {
            SyncEnabled = true,
            AutoSyncEnabled = false,
            AutoSyncPeriod = ShopifyDefaults.SynchronizationTask.Period / 60,
            PriceSyncEnabled = true,
            ImageSyncEnabled = true,
            InventorySyncEnabled = true,
            AutoAddRecordsEnabled = true
        });

        if (await _scheduleTaskService.GetTaskByTypeAsync(ShopifyDefaults.SynchronizationTask.Type) is null)
        {
            await _scheduleTaskService.InsertTaskAsync(new()
            {
                Enabled = false,
                StopOnError = false,
                LastEnabledUtc = DateTime.UtcNow,
                Name = ShopifyDefaults.SynchronizationTask.Name,
                Type = ShopifyDefaults.SynchronizationTask.Type,
                Seconds = ShopifyDefaults.SynchronizationTask.Period
            });
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.Shopify.Credentials"] = "Credentials",
            ["Plugins.Misc.Shopify.Credentials.Hint"] = "Create a custom app in your Shopify admin and generate an Admin API access token with read/write products and inventory permissions.",
            ["Plugins.Misc.Shopify.Configuration"] = "Configuration",
            ["Plugins.Misc.Shopify.Synchronization"] = "Synchronization",
            ["Plugins.Misc.Shopify.SyncNow"] = "Sync now",
            ["Plugins.Misc.Shopify.TestConnection"] = "Test connection",
            ["Plugins.Misc.Shopify.ConnectionSuccess"] = "Successfully connected to Shopify.",
            ["Plugins.Misc.Shopify.ConnectionFailed"] = "Connection failed: {0}",
            ["Plugins.Misc.Shopify.SyncStarted"] = "Synchronization started.",
            ["Plugins.Misc.Shopify.Fields.ShopDomain"] = "Shop domain",
            ["Plugins.Misc.Shopify.Fields.ShopDomain.Hint"] = "Enter your Shopify shop domain (e.g. your-store.myshopify.com).",
            ["Plugins.Misc.Shopify.Fields.ShopDomain.Required"] = "Shop domain is required",
            ["Plugins.Misc.Shopify.Fields.AccessToken"] = "Admin API access token",
            ["Plugins.Misc.Shopify.Fields.AccessToken.Hint"] = "Enter the Admin API access token from your Shopify custom app.",
            ["Plugins.Misc.Shopify.Fields.AccessToken.Required"] = "Access token is required",
            ["Plugins.Misc.Shopify.Fields.SyncEnabled"] = "Sync enabled",
            ["Plugins.Misc.Shopify.Fields.SyncEnabled.Hint"] = "Determine whether to synchronize products to Shopify.",
            ["Plugins.Misc.Shopify.Fields.AutoSyncEnabled"] = "Enable auto synchronization",
            ["Plugins.Misc.Shopify.Fields.AutoSyncEnabled.Hint"] = "Automatically queue product changes for synchronization and run the scheduled sync task.",
            ["Plugins.Misc.Shopify.Fields.AutoSyncPeriod"] = "Auto synchronization period",
            ["Plugins.Misc.Shopify.Fields.AutoSyncPeriod.Hint"] = "Set the period (in minutes) for auto synchronization.",
            ["Plugins.Misc.Shopify.Fields.AutoSyncPeriod.Invalid"] = "Period is invalid",
            ["Plugins.Misc.Shopify.Fields.PriceSyncEnabled"] = "Price sync enabled",
            ["Plugins.Misc.Shopify.Fields.PriceSyncEnabled.Hint"] = "Synchronize product prices to Shopify.",
            ["Plugins.Misc.Shopify.Fields.ImageSyncEnabled"] = "Image sync enabled",
            ["Plugins.Misc.Shopify.Fields.ImageSyncEnabled.Hint"] = "Synchronize the primary product image to Shopify.",
            ["Plugins.Misc.Shopify.Fields.InventorySyncEnabled"] = "Inventory sync enabled",
            ["Plugins.Misc.Shopify.Fields.InventorySyncEnabled.Hint"] = "Synchronize stock quantities to Shopify.",
            ["Plugins.Misc.Shopify.Fields.AutoAddRecordsEnabled"] = "Auto add records",
            ["Plugins.Misc.Shopify.Fields.AutoAddRecordsEnabled.Hint"] = "Automatically add new simple products to the sync queue.",
            ["Plugins.Misc.Shopify.Fields.ShopName"] = "Connected shop",
            ["Plugins.Misc.Shopify.Fields.LastSyncUtc"] = "Last sync",
            ["Plugins.Misc.Shopify.Fields.LastSyncError"] = "Last sync error",
            ["Plugins.Misc.Shopify.Fields.PendingRecords"] = "Pending records",
            ["Plugins.Misc.Shopify.Fields.NotConfigured"] = "Not configured",
            ["Plugins.Misc.Shopify.Fields.Connected"] = "Connected"
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<ShopifySettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.Shopify");

        var scheduleTask = await _scheduleTaskService.GetTaskByTypeAsync(ShopifyDefaults.SynchronizationTask.Type);
        if (scheduleTask is not null)
            await _scheduleTaskService.DeleteTaskAsync(scheduleTask);

        await base.UninstallAsync();
    }

    #endregion
}
