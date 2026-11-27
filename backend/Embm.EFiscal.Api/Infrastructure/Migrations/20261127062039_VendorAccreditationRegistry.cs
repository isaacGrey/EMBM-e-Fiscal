using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VendorAccreditationRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccreditationVersions",
                columns: table => new
                {
                    Category = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Filename = table.Column<string>(type: "text", nullable: true),
                    ContentType = table.Column<string>(type: "text", nullable: true),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    Size = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccreditationVersions", x => new { x.Category, x.Key, x.Number });
                    table.CheckConstraint("CK_Accreditation_Category", "\"Number\" > 0 AND \"Category\" IN ('Application','Checklist','Requirement','MaterialChange')");
                    table.CheckConstraint("CK_Accreditation_Evidence", "(\"Filename\" IS NULL AND \"ContentType\" IS NULL AND \"Size\" IS NULL AND \"Sha256\" IS NULL) OR\n(\"Filename\" IS NOT NULL AND \"ContentType\" IS NOT NULL AND \"Size\" IS NOT NULL AND\n \"Size\" BETWEEN 1 AND 2097152 AND \"Sha256\" IS NOT NULL AND length(\"Sha256\") = 64)");
                });

            migrationBuilder.CreateTable(
                name: "AccreditationFiles",
                columns: table => new
                {
                    Category = table.Column<string>(type: "character varying(24)", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccreditationFiles", x => new { x.Category, x.Key, x.Number });
                    table.CheckConstraint("CK_AccreditationFile_Size", " octet_length(\"Content\") BETWEEN 1 AND 2097152 ");
                    table.ForeignKey(
                        name: "FK_AccreditationFiles_AccreditationVersions_Category_Key_Number",
                        columns: x => new { x.Category, x.Key, x.Number },
                        principalTable: "AccreditationVersions",
                        principalColumns: new[] { "Category", "Key", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.Sql("""
                INSERT INTO "VendorConnectorGrants" ("UserId", "Permission")
                SELECT g."UserId", p.permission FROM "VendorConnectorGrants" g
                JOIN "Users" u ON u."Id" = g."UserId"
                CROSS JOIN (VALUES ('Accreditation.View'), ('Accreditation.Edit')) AS p(permission)
                WHERE g."Permission" = 'Connectors.Publish' AND u."Kind" = 'Vendor' AND u."DevelopmentFixture"
                ON CONFLICT DO NOTHING;
                CREATE TRIGGER accreditation_version_immutable BEFORE UPDATE OR DELETE ON "AccreditationVersions"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                CREATE TRIGGER accreditation_file_immutable BEFORE UPDATE OR DELETE ON "AccreditationFiles"
                    FOR EACH ROW EXECUTE FUNCTION efiscal_regulatory_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "VendorConnectorGrants" WHERE "Permission" IN ('Accreditation.View', 'Accreditation.Edit');
                """);
            migrationBuilder.DropTable(
                name: "AccreditationFiles");

            migrationBuilder.DropTable(
                name: "AccreditationVersions");
        }
    }
}
