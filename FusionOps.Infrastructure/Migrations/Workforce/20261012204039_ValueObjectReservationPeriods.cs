using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations.Workforce
{
    /// <inheritdoc />
    public partial class ValueObjectReservationPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tracking-only change: immutable Period becomes a complex value.
            // Existing PeriodStart/PeriodEnd columns and their data stay intact.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No physical schema change to reverse.
        }
    }
}
