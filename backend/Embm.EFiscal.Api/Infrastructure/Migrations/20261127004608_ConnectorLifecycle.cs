using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConnectorLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Lifecycle",
                table: "Connectors",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Connector_Lifecycle",
                table: "Connectors",
                sql: "\"Lifecycle\" IS NULL OR \"Lifecycle\" IN ('Deprecated', 'Retired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Connector_Lifecycle",
                table: "Connectors");

            migrationBuilder.DropColumn(
                name: "Lifecycle",
                table: "Connectors");
        }
    }
}
