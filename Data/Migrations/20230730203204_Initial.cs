using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoviSad.SokoBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "passenger",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    nickname = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passenger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "train",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    number = table.Column<int>(type: "INTEGER", nullable: false),
                    direction = table.Column<int>(type: "INTEGER", nullable: false),
                    departure_time = table.Column<long>(type: "INTEGER", nullable: false),
                    arrival_time = table.Column<long>(type: "INTEGER", nullable: false),
                    tag = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_train", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "PassengerDtoTrainDto",
                columns: table => new
                {
                    PassengersId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrainsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassengerDtoTrainDto", x => new { x.PassengersId, x.TrainsId });
                    table.ForeignKey(
                        name: "FK_PassengerDtoTrainDto_passenger_PassengersId",
                        column: x => x.PassengersId,
                        principalTable: "passenger",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PassengerDtoTrainDto_train_TrainsId",
                        column: x => x.TrainsId,
                        principalTable: "train",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_passenger_nickname",
                table: "passenger",
                column: "nickname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PassengerDtoTrainDto_TrainsId",
                table: "PassengerDtoTrainDto",
                column: "TrainsId");

            migrationBuilder.CreateIndex(
                name: "IX_train_arrival_time",
                table: "train",
                column: "arrival_time");

            migrationBuilder.CreateIndex(
                name: "IX_train_departure_time",
                table: "train",
                column: "departure_time");

            migrationBuilder.CreateIndex(
                name: "IX_train_id_departure_time",
                table: "train",
                columns: new[] { "id", "departure_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PassengerDtoTrainDto");

            migrationBuilder.DropTable(
                name: "passenger");

            migrationBuilder.DropTable(
                name: "train");
        }
    }
}
