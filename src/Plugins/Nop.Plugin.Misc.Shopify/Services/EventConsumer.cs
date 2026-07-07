using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Plugin.Misc.Shopify.Domain;
using Nop.Services.Catalog;
using Nop.Services.Events;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Misc.Shopify.Services;

/// <summary>
/// Represents plugin event consumer
/// </summary>
public class EventConsumer :
    BaseAdminMenuCreatedEventConsumer,
    IConsumer<EntityInsertedEvent<Product>>,
    IConsumer<EntityUpdatedEvent<Product>>,
    IConsumer<EntityDeletedEvent<Product>>,
    IConsumer<EntityUpdatedEvent<ProductPicture>>,
    IConsumer<EntityInsertedEvent<StockQuantityHistory>>
{
    #region Fields

    protected readonly IPermissionService _permissionService;
    protected readonly IProductService _productService;
    protected readonly ShopifyRecordService _shopifyRecordService;
    protected readonly ShopifySettings _shopifySettings;

    #endregion

    #region Ctor

    public EventConsumer(IPermissionService permissionService,
        IPluginManager<IPlugin> pluginManager,
        IProductService productService,
        ShopifyRecordService shopifyRecordService,
        ShopifySettings shopifySettings) :
        base(pluginManager)
    {
        _permissionService = permissionService;
        _productService = productService;
        _shopifyRecordService = shopifyRecordService;
        _shopifySettings = shopifySettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Checks is the current customer has rights to access this menu item
    /// </summary>
    protected override async Task<bool> CheckAccessAsync()
    {
        return await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handle entity created event
    /// </summary>
    public async Task HandleEventAsync(EntityInsertedEvent<Product> eventMessage)
    {
        if (eventMessage.Entity is null || !ShopifyService.IsConfigured(_shopifySettings) || !_shopifySettings.AutoSyncEnabled)
            return;

        await _shopifyRecordService.CreateOrUpdateRecordAsync(OperationType.Create, eventMessage.Entity.Id);
    }

    /// <summary>
    /// Handle entity updated event
    /// </summary>
    public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage)
    {
        if (eventMessage.Entity is null || !ShopifyService.IsConfigured(_shopifySettings) || !_shopifySettings.AutoSyncEnabled)
            return;

        if (!eventMessage.Entity.Deleted)
            await _shopifyRecordService.CreateOrUpdateRecordAsync(OperationType.Update, eventMessage.Entity.Id);
        else
            await _shopifyRecordService.CreateOrUpdateRecordAsync(OperationType.Delete, eventMessage.Entity.Id);
    }

    /// <summary>
    /// Handle entity deleted event
    /// </summary>
    public async Task HandleEventAsync(EntityDeletedEvent<Product> eventMessage)
    {
        if (eventMessage.Entity is null || !ShopifyService.IsConfigured(_shopifySettings) || !_shopifySettings.AutoSyncEnabled)
            return;

        await _shopifyRecordService.CreateOrUpdateRecordAsync(OperationType.Delete, eventMessage.Entity.Id);
    }

    /// <summary>
    /// Handle product picture updated event
    /// </summary>
    public async Task HandleEventAsync(EntityUpdatedEvent<ProductPicture> eventMessage)
    {
        if (eventMessage.Entity is null || !ShopifyService.IsConfigured(_shopifySettings) || !_shopifySettings.AutoSyncEnabled)
            return;

        await _shopifyRecordService.CreateOrUpdateRecordAsync(OperationType.Update, eventMessage.Entity.ProductId);
    }

    /// <summary>
    /// Handle stock quantity history created event
    /// </summary>
    public async Task HandleEventAsync(EntityInsertedEvent<StockQuantityHistory> eventMessage)
    {
        if (eventMessage.Entity is null || !ShopifyService.IsConfigured(_shopifySettings) || !_shopifySettings.AutoSyncEnabled)
            return;

        var productId = eventMessage.Entity.ProductId;
        if (productId <= 0)
            return;

        var record = await _shopifyRecordService.GetRecordByProductIdAsync(productId);
        if (record is null || record.ShopifyInventoryItemId <= 0)
            return;

        await _shopifyRecordService.CreateOrUpdateRecordAsync(OperationType.InventoryChanged, productId);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    protected override string PluginSystemName => ShopifyDefaults.SystemName;

    /// <summary>
    /// Menu item insertion type
    /// </summary>
    protected override MenuItemInsertType InsertType => MenuItemInsertType.After;

    /// <summary>
    /// The system name of the menu item after with need to insert the current one
    /// </summary>
    protected override string AfterMenuSystemName => "Shipping";

    #endregion
}
