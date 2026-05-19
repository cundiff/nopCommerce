using Nop.Core.Domain.Orders;
using Nop.Data;

namespace Nop.Services.Orders;

/// <summary>
/// Custom wishlist service
/// </summary>
public partial class CustomWishlistService : ICustomWishlistService
{
    #region Fields

    protected readonly IRepository<CustomWishlist> _customWishlistRepository;
    protected readonly ShoppingCartSettings _shoppingCartSettings;
    protected readonly IRepository<WishlistShare> _wishlistShareRepository;

    #endregion

    #region Ctor

    public CustomWishlistService(IRepository<CustomWishlist> customWishlistRepository, 
        ShoppingCartSettings shoppingCartSettings,
        IRepository<WishlistShare> wishlistShareRepository)
    {
        _customWishlistRepository = customWishlistRepository;
        _shoppingCartSettings = shoppingCartSettings;
        _wishlistShareRepository = wishlistShareRepository;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Retrieves all custom wishlists associated with the specified customer.
    /// </summary>
    /// <param name="customerId">The unique identifier of the customer whose custom wishlists are to be retrieved.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of  <see
    /// cref="CustomWishlist"/> objects associated with the specified customer. If multiple wishlists  are not allowed,
    /// an empty list is returned.</returns>
    public virtual async Task<IList<CustomWishlist>> GetAllCustomWishlistsAsync(int customerId)
    {
        if (!_shoppingCartSettings.AllowMultipleWishlist)
            return new List<CustomWishlist>();

        var query = _customWishlistRepository.Table
            .Where(w => w.CustomerId == customerId)
            .OrderByDescending(w => w.CreatedOnUtc);
        return await query.ToListAsync();
    }

    /// <summary>
    /// Adds a custom wishlist item to the repository.
    /// </summary>
    /// <param name="item">The custom wishlist item to add. Cannot be <see langword="null"/>.</param>
    public virtual async Task AddCustomWishlistAsync(CustomWishlist item)
    {
        await _customWishlistRepository.InsertAsync(item);
    }

    /// <summary>
    /// Removes a custom wishlist item with the specified identifier.
    /// </summary>
    /// <param name="itemId">The unique identifier of the custom wishlist item to remove. Must be a valid identifier of an existing item.</param>
    public virtual async Task RemoveCustomWishlistAsync(int itemId)
    {
        var item = await _customWishlistRepository.GetByIdAsync(itemId);
        if (item != null)
            await _customWishlistRepository.DeleteAsync(item);
    }

    /// <summary>
    /// Updates an existing custom wishlist in the data store if it exists.
    /// </summary>
    /// <param name="item">The custom wishlist to update. The wishlist must have a valid identifier corresponding to an existing entry.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    public virtual async Task UpdateCustomWishlistAsync(CustomWishlist item)
    {
        var customWishlist = await _customWishlistRepository.GetByIdAsync(item.Id);
        if (customWishlist != null)
        {
            await _customWishlistRepository.UpdateAsync(item);
        }
    }

    /// <summary>
    /// Retrieves a custom wishlist by its unique identifier.
    /// </summary>
    /// <param name="itemId">The unique identifier of the custom wishlist to retrieve. Must be a positive integer.</param>
    /// <returns>A <see cref="CustomWishlist"/> object representing the custom wishlist with the specified identifier. Returns
    /// null if no wishlist is found with the given identifier.</returns>
    public virtual async Task<CustomWishlist> GetCustomWishlistByIdAsync(int itemId)
    {
        return await _customWishlistRepository.GetByIdAsync(itemId);
    }

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
    public virtual async Task<WishlistShare> GenerateWishlistShareAsync(int customerId, int? customWishlistId, int expirationDays)
    {
        if (customerId <= 0)
            throw new ArgumentException("Customer identifier should be positive.", nameof(customerId));

        if (!NopOrderDefaults.WishlistShareExpirationDays.Contains(expirationDays))
            throw new ArgumentException("Unsupported wishlist share expiration period.", nameof(expirationDays));

        if (customWishlistId.HasValue)
        {
            var customWishlist = await GetCustomWishlistByIdAsync(customWishlistId.Value);
            if (customWishlist == null || customWishlist.CustomerId != customerId)
                throw new InvalidOperationException("The custom wishlist cannot be shared by the specified customer.");
        }

        var now = DateTime.UtcNow;
        var wishlistShare = new WishlistShare
        {
            ShareGuid = Guid.NewGuid(),
            CustomerId = customerId,
            CustomWishlistId = customWishlistId,
            CreatedOnUtc = now,
            ExpiresOnUtc = now.AddDays(expirationDays)
        };

        await _wishlistShareRepository.InsertAsync(wishlistShare);

        return wishlistShare;
    }

    /// <summary>
    /// Retrieves a wishlist share by its public share token.
    /// </summary>
    /// <param name="shareGuid">The public share token.</param>
    /// <param name="onlyActive">Whether to return only non-expired shares.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the matching
    /// <see cref="WishlistShare"/>, or <see langword="null"/> if no matching active share exists.
    /// </returns>
    public virtual async Task<WishlistShare> GetWishlistShareByGuidAsync(Guid shareGuid, bool onlyActive = true)
    {
        if (shareGuid == Guid.Empty)
            return null;

        var query = _wishlistShareRepository.Table.Where(share => share.ShareGuid == shareGuid);
        if (onlyActive)
        {
            var now = DateTime.UtcNow;
            query = query.Where(share => share.ExpiresOnUtc > now);
        }

        return await query.FirstOrDefaultAsync();
    }

    #endregion
}
