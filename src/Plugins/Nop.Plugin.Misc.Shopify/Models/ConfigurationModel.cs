using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.Shopify.Models;

/// <summary>
/// Represents configuration model
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.ShopDomain")]
    public string ShopDomain { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.AccessToken")]
    public string AccessToken { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.SyncEnabled")]
    public bool SyncEnabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.AutoSyncEnabled")]
    public bool AutoSyncEnabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.AutoSyncPeriod")]
    public int AutoSyncPeriod { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.PriceSyncEnabled")]
    public bool PriceSyncEnabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.ImageSyncEnabled")]
    public bool ImageSyncEnabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.InventorySyncEnabled")]
    public bool InventorySyncEnabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.Shopify.Fields.AutoAddRecordsEnabled")]
    public bool AutoAddRecordsEnabled { get; set; }

    public string ShopName { get; set; }

    public string LastSyncUtc { get; set; }

    public string LastSyncError { get; set; }

    public int PendingRecords { get; set; }

    public bool IsConfigured { get; set; }
}
