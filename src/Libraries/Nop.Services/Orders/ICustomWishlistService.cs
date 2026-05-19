using Nop.Core.Domain.Orders;

namespace Nop.Services.Orders;

/// <summary>
/// Custom wishlist service interface
/// </summary>
public partial interface ICustomWishlistService
{
    /// <summary>
    /// Retrieves all custom wishlists associated with the specified customer.
    /// </summary>
    /// <param name="customerId">The unique identifier of the customer whose custom wishlists are to be retrieved.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of  <see
    /// cref="CustomWishlist"/> objects associated with the specified customer. If multiple wishlists  are not allowed,
    /// an empty list is returned.</returns>
    Task<IList<CustomWishlist>> GetAllCustomWishlistsAsync(int customerId);

    /// <summary>
    /// Adds a custom wishlist item to the repository.
    /// </summary>
    /// <param name="item">The custom wishlist item to add. Cannot be <see langword="null"/>.</param>
    Task AddCustomWishlistAsync(CustomWishlist item);

    /// <summary>
    /// Removes a custom wishlist item with the specified identifier.
    /// </summary>
    /// <param name="itemId">The unique identifier of the custom wishlist item to remove. Must be a valid identifier of an existing item.</param>
    Task RemoveCustomWishlistAsync(int itemId);

    /// <summary>
    /// Updates an existing custom wishlist in the data store if it exists.
    /// </summary>
    /// <param name="item">The custom wishlist to update. The wishlist must have a valid identifier corresponding to an existing entry.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    Task UpdateCustomWishlistAsync(CustomWishlist item);

    /// <summary>
    /// Retrieves a custom wishlist by its unique identifier.
    /// </summary>
    /// <param name="itemId">The unique identifier of the custom wishlist to retrieve. Must be a positive integer.</param>
    /// <returns>A <see cref="CustomWishlist"/> object representing the custom wishlist with the specified identifier. Returns
    /// null if no wishlist is found with the given identifier.</returns>
    Task<CustomWishlist> GetCustomWishlistByIdAsync(int itemId);

    /// <summary>
    /// Generates a public wishlist share token for the specified customer.
    /// </summary>
    /// <param name="customerId">The unique identifier of the customer who owns the wishlist.</param>
    /// <param name="customWishlistId">The optional custom wishlist identifier. If provided, it must belong to the specified customer.</param>
    /// <param name="expirationDays">The number of days until the share expires. Must be one of the supported wishlist share expiration values.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the created
    /// <see cref="WishlistShare"/>.
    /// </returns>
    Task<WishlistShare> GenerateWishlistShareAsync(int customerId, int? customWishlistId, int expirationDays);

    /// <summary>
    /// Retrieves a wishlist share by its public share token.
    /// </summary>
    /// <param name="shareGuid">The public share token.</param>
    /// <param name="onlyActive">Whether to return only non-expired shares.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the matching
    /// <see cref="WishlistShare"/>, or <see langword="null"/> if no matching active share exists.
    /// </returns>
    Task<WishlistShare> GetWishlistShareByGuidAsync(Guid shareGuid, bool onlyActive = true);
}
