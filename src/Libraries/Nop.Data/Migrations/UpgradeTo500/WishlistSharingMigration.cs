using FluentMigrator;
using Nop.Core.Domain.Orders;
using Nop.Data.Extensions;

namespace Nop.Data.Migrations.UpgradeTo500;

[NopSchemaMigration("2026-05-19 00:00:00", "Wishlist sharing")]
public class WishlistSharingMigration : ForwardOnlyMigration
{
    /// <summary>
    /// Collect the UP migration expressions
    /// </summary>
    public override void Up()
    {
        this.CreateTableIfNotExists<WishlistShare>();

        var tableName = nameof(WishlistShare);
        var shareGuidIndexName = "IX_WishlistShare_ShareGuid";

        if (!Schema.Table(tableName).Index(shareGuidIndexName).Exists())
        {
            Create.Index(shareGuidIndexName)
                .OnTable(tableName)
                .OnColumn(nameof(WishlistShare.ShareGuid)).Ascending()
                .WithOptions().Unique();
        }
    }
}
