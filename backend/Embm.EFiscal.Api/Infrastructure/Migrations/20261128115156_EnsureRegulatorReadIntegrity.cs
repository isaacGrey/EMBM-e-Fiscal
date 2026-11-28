using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embm.EFiscal.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnsureRegulatorReadIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE TRIGGER regulator_read_audit_immutable BEFORE UPDATE OR DELETE ON "RegulatorReadAudits"
                    FOR EACH ROW EXECUTE FUNCTION connect_phase8_immutable();

                CREATE OR REPLACE FUNCTION regulator_grant_revocation_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD."RevokedAt" IS NULL AND NEW."RevokedAt" IS NOT NULL
                            AND (to_jsonb(NEW) - 'RevokedAt') = (to_jsonb(OLD) - 'RevokedAt') THEN
                            RETURN NEW;
                        END IF;
                    END IF;
                    RAISE EXCEPTION 'Regulator grants may only be revoked';
                END
                $$;
                CREATE OR REPLACE TRIGGER regulator_grant_revocation_only BEFORE UPDATE OR DELETE ON "RegulatorGrants"
                    FOR EACH ROW EXECUTE FUNCTION regulator_grant_revocation_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The preceding migration also defines these protections for fresh databases.
        }
    }
}
