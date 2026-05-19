namespace Nop.Core.Domain.Orders;

/// <summary>
/// Represents a public wishlist sharing link
/// </summary>
public partial class WishlistShare : BaseEntity
{
    /// <summary>
    /// Gets or sets the public share token
    /// </summary>
    public Guid ShareGuid { get; set; }

    /// <summary>
    /// Gets or sets the customer identifier
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the custom wishlist identifier
    /// </summary>
    public int? CustomWishlistId { get; set; }

    /// <summary>
    /// Gets or sets the date and time of instance creation
    /// </summary>
    public DateTime CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the share expires
    /// </summary>
    public DateTime ExpiresOnUtc { get; set; }
}
