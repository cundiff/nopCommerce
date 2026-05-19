using Nop.Web.Framework.Models;

namespace Nop.Plugin.Widgets.WishlistSharing.Models;

/// <summary>
/// Represents public wishlist sharing widget model
/// </summary>
public record PublicInfoModel : BaseNopModel
{
    public PublicInfoModel()
    {
        ExpirationDays = new List<int>();
    }

    /// <summary>
    /// Gets or sets the custom wishlist identifier
    /// </summary>
    public int? ListId { get; set; }

    /// <summary>
    /// Gets or sets supported expiration periods in days
    /// </summary>
    public IList<int> ExpirationDays { get; set; }
}
