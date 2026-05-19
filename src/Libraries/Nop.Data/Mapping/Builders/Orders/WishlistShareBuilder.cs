using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data.Extensions;

namespace Nop.Data.Mapping.Builders.Orders;

/// <summary>
/// Represents a wishlist share entity builder
/// </summary>
public partial class WishlistShareBuilder : NopEntityBuilder<WishlistShare>
{
    #region Methods

    /// <summary>
    /// Apply entity configuration
    /// </summary>
    /// <param name="table">Create table expression builder</param>
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(WishlistShare.CustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(WishlistShare.CustomWishlistId)).AsInt32().Nullable().ForeignKey<CustomWishlist>(onDelete: Rule.None);
    }

    #endregion
}
