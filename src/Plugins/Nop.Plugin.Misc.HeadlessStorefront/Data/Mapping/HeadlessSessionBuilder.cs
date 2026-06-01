using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.HeadlessStorefront.Domain;

namespace Nop.Plugin.Misc.HeadlessStorefront.Data.Mapping;

public class HeadlessSessionBuilder : NopEntityBuilder<HeadlessSession>
{
  public override void MapEntity(CreateTableExpressionBuilder table)
  {
    table
      .WithColumn(nameof(HeadlessSession.SessionToken)).AsString(64).NotNullable()
      .WithColumn(nameof(HeadlessSession.CustomerId)).AsInt32().NotNullable()
      .WithColumn(nameof(HeadlessSession.CheckoutHandoffToken)).AsString(64).Nullable()
      .WithColumn(nameof(HeadlessSession.CheckoutHandoffExpiresUtc)).AsDateTime2().Nullable()
      .WithColumn(nameof(HeadlessSession.CreatedOnUtc)).AsDateTime2().NotNullable();
  }
}
