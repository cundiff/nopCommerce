namespace Nop.Plugin.Misc.Shopify.Domain;

/// <summary>
/// Represents an operation type enumeration
/// </summary>
public enum OperationType
{
    /// <summary>
    /// None
    /// </summary>
    None,

    /// <summary>
    /// Create
    /// </summary>
    Create,

    /// <summary>
    /// Update
    /// </summary>
    Update,

    /// <summary>
    /// Delete
    /// </summary>
    Delete,

    /// <summary>
    /// Inventory changed
    /// </summary>
    InventoryChanged
}
