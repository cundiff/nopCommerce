using Microsoft.Extensions.Logging;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.Shopify.Domain;
using Nop.Plugin.Misc.Shopify.Domain.Api;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Media;

namespace Nop.Plugin.Misc.Shopify.Services;

/// <summary>
/// Represents the main Shopify plugin service
/// </summary>
public class ShopifyService
{
    #region Fields

    protected readonly ILogger<ShopifyService> _logger;
    protected readonly IPictureService _pictureService;
    protected readonly IProductAttributeService _productAttributeService;
    protected readonly IProductService _productService;
    protected readonly ISettingService _settingService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWebHelper _webHelper;
    protected readonly ShopifyHttpClient _shopifyHttpClient;
    protected readonly ShopifyRecordService _shopifyRecordService;
    protected readonly ShopifySettings _shopifySettings;

    #endregion

    #region Ctor

    public ShopifyService(ILogger<ShopifyService> logger,
        IPictureService pictureService,
        IProductAttributeService productAttributeService,
        IProductService productService,
        ISettingService settingService,
        IStoreContext storeContext,
        IWebHelper webHelper,
        ShopifyHttpClient shopifyHttpClient,
        ShopifyRecordService shopifyRecordService,
        ShopifySettings shopifySettings)
    {
        _logger = logger;
        _pictureService = pictureService;
        _productAttributeService = productAttributeService;
        _productService = productService;
        _settingService = settingService;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _shopifyHttpClient = shopifyHttpClient;
        _shopifyRecordService = shopifyRecordService;
        _shopifySettings = shopifySettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Check whether the plugin is configured
    /// </summary>
    public static bool IsConfigured(ShopifySettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings?.ShopDomain) &&
            !string.IsNullOrWhiteSpace(settings?.AccessToken);
    }

    /// <summary>
    /// Check whether the product can be synchronized
    /// </summary>
    protected async Task<bool> CanSyncProductAsync(Product product)
    {
        if (product is null || product.Deleted)
            return false;

        if (product.ProductType != ProductType.SimpleProduct)
            return false;

        var attributeMappings = await _productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id);
        if (attributeMappings.Any())
            return false;

        return true;
    }

    /// <summary>
    /// Get product image URL
    /// </summary>
    protected async Task<string> GetProductImageUrlAsync(Product product)
    {
        if (!_shopifySettings.ImageSyncEnabled)
            return null;

        var picture = (await _pictureService.GetPicturesByProductIdAsync(product.Id, 1)).FirstOrDefault();
        if (picture is null)
            return null;

        var (url, _) = await _pictureService.GetPictureUrlAsync(picture);
        var storeLocation = _webHelper.GetStoreLocation();

        if (!string.IsNullOrEmpty(url) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = storeLocation.TrimEnd('/') + url;

        return url;
    }

    /// <summary>
    /// Prepare Shopify product payload
    /// </summary>
    protected async Task<ProductRequest> PrepareProductRequestAsync(Product product, ShopifyRecord record)
    {
        var imageUrl = await GetProductImageUrlAsync(product);
        var store = await _storeContext.GetCurrentStoreAsync();

        var request = new ProductRequest
        {
            Product = new ProductPayload
            {
                Title = product.Name,
                BodyHtml = product.FullDescription,
                Vendor = store?.Name ?? "nopCommerce",
                ProductType = "General",
                Status = product.Published ? "active" : "draft",
                Variants = new List<VariantPayload>
                {
                    new()
                    {
                        Id = record.ShopifyVariantId > 0 ? record.ShopifyVariantId : null,
                        Sku = product.Sku,
                        Price = _shopifySettings.PriceSyncEnabled ? product.Price.ToString("0.00") : null,
                        InventoryManagement = _shopifySettings.InventorySyncEnabled ? "shopify" : null,
                        InventoryQuantity = _shopifySettings.InventorySyncEnabled ? product.StockQuantity : null
                    }
                }
            }
        };

        if (!string.IsNullOrEmpty(imageUrl))
        {
            request.Product.Images = new List<ImagePayload>
            {
                new() { Src = imageUrl }
            };
        }

        return request;
    }

    /// <summary>
    /// Update record from Shopify product response
    /// </summary>
    protected static void UpdateRecordFromProduct(ShopifyRecord record, ProductPayload shopifyProduct)
    {
        record.ShopifyProductId = shopifyProduct.Id ?? 0;

        var variant = shopifyProduct.Variants?.FirstOrDefault();
        if (variant is not null)
        {
            record.ShopifyVariantId = variant.Id ?? 0;
            record.ShopifyInventoryItemId = variant.InventoryItemId ?? 0;
        }

        record.OperationType = OperationType.None;
        record.UpdatedOnUtc = DateTime.UtcNow;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Test connection and populate shop details
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            var shop = await _shopifyHttpClient.GetShopAsync();
            if (shop is null)
                return (false, "Unable to retrieve shop details from Shopify.");

            _shopifySettings.ShopName = shop.Name;
            await EnsureLocationIdAsync();

            await _settingService.SaveSettingAsync(_shopifySettings);

            return (true, $"Connected to {shop.Name}.");
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    /// <summary>
    /// Ensure Shopify location identifier is set
    /// </summary>
    public async Task EnsureLocationIdAsync()
    {
        if (_shopifySettings.ShopifyLocationId.HasValue)
            return;

        var locations = await _shopifyHttpClient.GetLocationsAsync();
        var location = locations.FirstOrDefault(item => item.Active) ?? locations.FirstOrDefault();

        if (location is null)
            throw new NopException("No Shopify locations found.");

        _shopifySettings.ShopifyLocationId = location.Id;
        await _settingService.SaveSettingAsync(_shopifySettings);
    }

    /// <summary>
    /// Synchronize pending records to Shopify
    /// </summary>
    public async Task SyncAsync(bool addMissingRecords = true)
    {
        if (!IsConfigured(_shopifySettings) || !_shopifySettings.SyncEnabled)
            return;

        try
        {
            await EnsureLocationIdAsync();

            if (addMissingRecords)
                await _shopifyRecordService.AddRecordsForPublishedProductsAsync();

            var operationTypes = new List<OperationType>
            {
                OperationType.Create,
                OperationType.Update,
                OperationType.Delete,
                OperationType.InventoryChanged
            };

            var records = await _shopifyRecordService.GetAllRecordsAsync(active: true, operationTypes: operationTypes);

            foreach (var record in records)
            {
                try
                {
                    await ProcessRecordAsync(record);
                    await Task.Delay(ShopifyDefaults.SyncRequestDelayMs);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Shopify sync failed for product #{ProductId}", record.ProductId);
                    _shopifySettings.LastSyncError = $"Product #{record.ProductId}: {exception.Message}";
                }
            }

            _shopifySettings.LastSyncUtc = DateTime.UtcNow;
            if (records.Any())
                _shopifySettings.LastSyncError = null;

            await _settingService.SaveSettingAsync(_shopifySettings);
        }
        catch (Exception exception)
        {
            _shopifySettings.LastSyncError = exception.Message;
            await _settingService.SaveSettingAsync(_shopifySettings);
            throw;
        }
    }

    /// <summary>
    /// Process a single synchronization record
    /// </summary>
    protected async Task ProcessRecordAsync(ShopifyRecord record)
    {
        switch (record.OperationType)
        {
            case OperationType.Delete:
                await DeleteProductAsync(record);
                break;

            case OperationType.InventoryChanged:
                await SyncInventoryAsync(record);
                break;

            case OperationType.Create:
            case OperationType.Update:
                await SyncProductAsync(record);
                break;
        }
    }

    /// <summary>
    /// Create or update a product in Shopify
    /// </summary>
    protected async Task SyncProductAsync(ShopifyRecord record)
    {
        var product = await _productService.GetProductByIdAsync(record.ProductId);
        if (!await CanSyncProductAsync(product))
        {
            if (record.OperationType == OperationType.Create)
                await _shopifyRecordService.DeleteRecordAsync(record);

            return;
        }

        var request = await PrepareProductRequestAsync(product, record);

        ProductPayload shopifyProduct;
        if (record.ShopifyProductId > 0)
            shopifyProduct = await _shopifyHttpClient.UpdateProductAsync(record.ShopifyProductId, request);
        else
            shopifyProduct = await _shopifyHttpClient.CreateProductAsync(request);

        if (shopifyProduct is null)
            throw new NopException("Shopify returned an empty product response.");

        UpdateRecordFromProduct(record, shopifyProduct);
        await _shopifyRecordService.UpdateRecordAsync(record);

        if (_shopifySettings.InventorySyncEnabled && record.ShopifyInventoryItemId > 0)
            await SyncInventoryAsync(record, product.StockQuantity);
    }

    /// <summary>
    /// Delete a product in Shopify
    /// </summary>
    protected async Task DeleteProductAsync(ShopifyRecord record)
    {
        if (record.ShopifyProductId > 0)
            await _shopifyHttpClient.DeleteProductAsync(record.ShopifyProductId);

        await _shopifyRecordService.DeleteRecordAsync(record);
    }

    /// <summary>
    /// Synchronize inventory for a record
    /// </summary>
    protected async Task SyncInventoryAsync(ShopifyRecord record, int? stockQuantity = null)
    {
        if (!_shopifySettings.InventorySyncEnabled || record.ShopifyInventoryItemId <= 0)
        {
            record.OperationType = OperationType.None;
            record.UpdatedOnUtc = DateTime.UtcNow;
            await _shopifyRecordService.UpdateRecordAsync(record);
            return;
        }

        if (!stockQuantity.HasValue)
        {
            var product = await _productService.GetProductByIdAsync(record.ProductId);
            stockQuantity = product?.StockQuantity ?? 0;
        }

        await _shopifyHttpClient.SetInventoryLevelAsync(new InventoryLevelRequest
        {
            LocationId = _shopifySettings.ShopifyLocationId ?? 0,
            InventoryItemId = record.ShopifyInventoryItemId,
            Available = stockQuantity.Value
        });

        record.OperationType = OperationType.None;
        record.UpdatedOnUtc = DateTime.UtcNow;
        await _shopifyRecordService.UpdateRecordAsync(record);
    }

    #endregion
}
