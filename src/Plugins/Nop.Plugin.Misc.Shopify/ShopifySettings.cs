using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.Shopify;

/// <summary>
/// Represents plugin settings
/// </summary>
public class ShopifySettings : ISettings
{
    /// <summary>
    /// Gets or sets the Shopify shop domain (e.g. your-store.myshopify.com)
    /// </summary>
    public string ShopDomain { get; set; }

    /// <summary>
    /// Gets or sets the Shopify Admin API access token
    /// </summary>
    public string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether synchronization is enabled
    /// </summary>
    public bool SyncEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether auto synchronization is enabled
    /// </summary>
    public bool AutoSyncEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value how often (in minutes) auto synchronization will run
    /// </summary>
    public int AutoSyncPeriod { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to sync price for products
    /// </summary>
    public bool PriceSyncEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to sync images for products
    /// </summary>
    public bool ImageSyncEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to sync inventory for products
    /// </summary>
    public bool InventorySyncEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to auto add records for synchronization
    /// </summary>
    public bool AutoAddRecordsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the Shopify location identifier used for inventory updates
    /// </summary>
    public long? ShopifyLocationId { get; set; }

    /// <summary>
    /// Gets or sets the shop name returned by Shopify
    /// </summary>
    public string ShopName { get; set; }

    /// <summary>
    /// Gets or sets the date and time of the last synchronization (UTC)
    /// </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the last synchronization error message
    /// </summary>
    public string LastSyncError { get; set; }
}
