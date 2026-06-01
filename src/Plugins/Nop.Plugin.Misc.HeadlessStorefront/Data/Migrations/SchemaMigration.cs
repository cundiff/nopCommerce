using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Mapping;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.HeadlessStorefront.Domain;

namespace Nop.Plugin.Misc.HeadlessStorefront.Data.Migrations;

[NopMigration("2026-06-01 00:00:00", "Misc.HeadlessStorefront schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
  public override void Up()
  {
    if (!Schema.Table(NameCompatibilityManager.GetTableName(typeof(HeadlessSession))).Exists())
      Create.TableFor<HeadlessSession>();
  }

  public override void Down()
  {
    Delete.Table(NameCompatibilityManager.GetTableName(typeof(HeadlessSession)));
  }
}
