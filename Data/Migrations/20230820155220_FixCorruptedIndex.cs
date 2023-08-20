using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoviSad.SokoBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCorruptedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_train_id_departure_time",
                table: "train");

            migrationBuilder.CreateIndex(
                name: "IX_train_number_departure_time",
                table: "train",
                columns: new[] { "number", "departure_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_train_number_departure_time",
                table: "train");

            migrationBuilder.CreateIndex(
                name: "IX_train_id_departure_time",
                table: "train",
                columns: new[] { "id", "departure_time" },
                unique: true);
        }
    }
}
