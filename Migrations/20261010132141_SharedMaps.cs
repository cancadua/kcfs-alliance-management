using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllianceRewards.Api.Migrations
{
    /// <inheritdoc />
    public partial class SharedMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedMaps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedMaps_CodeHash",
                table: "SharedMaps",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedMaps_ExpiresAt",
                table: "SharedMaps",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedMaps");
        }
    }
}
