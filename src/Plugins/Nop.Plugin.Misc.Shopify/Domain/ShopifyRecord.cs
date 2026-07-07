using Nop.Core;

namespace Nop.Plugin.Misc.Shopify.Domain;

/// <summary>
/// Represents a record of the product and other details required for synchronization with Shopify
/// </summary>
public class ShopifyRecord : BaseEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether the record is active
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets the product identifier
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// Gets or sets the product attribute combination identifier (0 for simple products)
    /// </summary>
    public int CombinationId { get; set; }

    /// <summary>
    /// Gets or sets the Shopify product identifier
    /// </summary>
    public long ShopifyProductId { get; set; }

    /// <summary>
    /// Gets or sets the Shopify variant identifier
    /// </summary>
    public long ShopifyVariantId { get; set; }

    /// <summary>
    /// Gets or sets the Shopify inventory item identifier
    /// </summary>
    public long ShopifyInventoryItemId { get; set; }

    /// <summary>
    /// Gets or sets an operation type identifier
    /// </summary>
    public int OperationTypeId { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the record was updated
    /// </summary>
    public DateTime? UpdatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets an operation type
    /// </summary>
    public OperationType OperationType
    {
        get => (OperationType)OperationTypeId;
        set => OperationTypeId = (int)value;
    }
}
