using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.Shopify.Services;

/// <summary>
/// Represents a schedule task to synchronize products with Shopify
/// </summary>
public class ShopifySyncTask : IScheduleTask
{
    #region Fields

    protected readonly ShopifyService _shopifyService;
    protected readonly ShopifySettings _shopifySettings;

    #endregion

    #region Ctor

    public ShopifySyncTask(ShopifyService shopifyService,
        ShopifySettings shopifySettings)
    {
        _shopifyService = shopifyService;
        _shopifySettings = shopifySettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Execute task
    /// </summary>
    public async Task ExecuteAsync()
    {
        if (!ShopifyService.IsConfigured(_shopifySettings) || !_shopifySettings.AutoSyncEnabled)
            return;

        await _shopifyService.SyncAsync();
    }

    #endregion
}
