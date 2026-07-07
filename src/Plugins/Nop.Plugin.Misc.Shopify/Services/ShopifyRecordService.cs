using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Plugin.Misc.Shopify.Domain;

namespace Nop.Plugin.Misc.Shopify.Services;

/// <summary>
/// Represents the service to manage synchronization records
/// </summary>
public class ShopifyRecordService
{
    #region Fields

    protected readonly IRepository<Product> _productRepository;
    protected readonly IRepository<ShopifyRecord> _repository;
    protected readonly ShopifySettings _shopifySettings;

    #endregion

    #region Ctor

    public ShopifyRecordService(IRepository<Product> productRepository,
        IRepository<ShopifyRecord> repository,
        ShopifySettings shopifySettings)
    {
        _productRepository = productRepository;
        _repository = repository;
        _shopifySettings = shopifySettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Insert the record
    /// </summary>
    public async Task InsertRecordAsync(ShopifyRecord record)
    {
        await _repository.InsertAsync(record, false);
    }

    /// <summary>
    /// Update the record
    /// </summary>
    public async Task UpdateRecordAsync(ShopifyRecord record)
    {
        await _repository.UpdateAsync(record, false);
    }

    /// <summary>
    /// Delete the record
    /// </summary>
    public async Task DeleteRecordAsync(ShopifyRecord record)
    {
        await _repository.DeleteAsync(record, false);
    }

    /// <summary>
    /// Get all records for synchronization
    /// </summary>
    public async Task<IPagedList<ShopifyRecord>> GetAllRecordsAsync(bool? active = null,
        IList<OperationType> operationTypes = null,
        int pageIndex = 0, int pageSize = int.MaxValue)
    {
        return await _repository.GetAllPagedAsync(query =>
        {
            if (active.HasValue)
                query = query.Where(record => record.Active == active.Value);

            if (operationTypes?.Any() ?? false)
                query = query.Where(record => operationTypes.Contains(record.OperationType));

            return query.OrderBy(record => record.Id);
        }, pageIndex, pageSize);
    }

    /// <summary>
    /// Get record by product identifier
    /// </summary>
    public async Task<ShopifyRecord> GetRecordByProductIdAsync(int productId, int combinationId = 0)
    {
        return await _repository.Table
            .FirstOrDefaultAsync(record => record.ProductId == productId && record.CombinationId == combinationId);
    }

    /// <summary>
    /// Get pending records count
    /// </summary>
    public async Task<int> GetPendingRecordsCountAsync()
    {
        return await _repository.Table
            .CountAsync(record => record.Active && record.OperationType != OperationType.None);
    }

    /// <summary>
    /// Create or update a record for synchronization
    /// </summary>
    public async Task CreateOrUpdateRecordAsync(OperationType operationType, int productId, int combinationId = 0)
    {
        if (productId <= 0)
            return;

        var existingRecord = await GetRecordByProductIdAsync(productId, combinationId);

        if (existingRecord is null)
        {
            if (operationType != OperationType.Create || !_shopifySettings.AutoAddRecordsEnabled)
                return;

            await InsertRecordAsync(new ShopifyRecord
            {
                Active = _shopifySettings.SyncEnabled,
                ProductId = productId,
                CombinationId = combinationId,
                OperationType = operationType
            });

            return;
        }

        switch (existingRecord.OperationType)
        {
            case OperationType.Create:
                if (operationType == OperationType.Delete)
                    await DeleteRecordAsync(existingRecord);
                return;

            case OperationType.Update:
            case OperationType.InventoryChanged:
                if (operationType == OperationType.Delete)
                {
                    existingRecord.OperationType = OperationType.Delete;
                    await UpdateRecordAsync(existingRecord);
                }
                else if (operationType == OperationType.InventoryChanged)
                {
                    existingRecord.OperationType = OperationType.InventoryChanged;
                    await UpdateRecordAsync(existingRecord);
                }
                else if (operationType != OperationType.Create)
                {
                    existingRecord.OperationType = OperationType.Update;
                    await UpdateRecordAsync(existingRecord);
                }
                return;

            case OperationType.Delete:
                if (operationType == OperationType.Create)
                {
                    existingRecord.OperationType = OperationType.Update;
                    await UpdateRecordAsync(existingRecord);
                }
                return;

            case OperationType.None:
                existingRecord.OperationType = operationType;
                await UpdateRecordAsync(existingRecord);
                return;
        }
    }

    /// <summary>
    /// Add records for published simple products
    /// </summary>
    public async Task<int> AddRecordsForPublishedProductsAsync()
    {
        var existingProductIds = await _repository.Table.Select(record => record.ProductId).Distinct().ToListAsync();
        var products = await _productRepository.GetAllAsync(query => query
            .Where(product => !product.Deleted && product.Published && product.ProductTypeId == (int)ProductType.SimpleProduct)
            .Where(product => !existingProductIds.Contains(product.Id)), null);

        var records = products.Select(product => new ShopifyRecord
        {
            Active = _shopifySettings.SyncEnabled,
            ProductId = product.Id,
            OperationType = OperationType.Create
        }).ToList();

        if (!records.Any())
            return 0;

        await _repository.InsertAsync(records, false);

        return records.Count;
    }

    #endregion
}
