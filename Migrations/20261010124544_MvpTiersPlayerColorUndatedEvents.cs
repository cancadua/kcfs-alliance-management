using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AllianceRewards.Api.Migrations
{
    /// <inheritdoc />
    public partial class MvpTiersPlayerColorUndatedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Date",
                table: "Events");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Players",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            // Every reward is an MVP now: Blue/Purple tiers are renamed, plain "Mvp" becomes the Normal tier.
            migrationBuilder.Sql("""
                UPDATE "Rewards" SET "Type" = CASE "Type"
                    WHEN 'Blue' THEN 'Earl'
                    WHEN 'Purple' THEN 'Duke'
                    WHEN 'Mvp' THEN 'Normal'
                    ELSE "Type" END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // "Mvp" rewards merged into Normal cannot be told apart anymore.
            migrationBuilder.Sql("""
                UPDATE "Rewards" SET "Type" = CASE "Type"
                    WHEN 'Earl' THEN 'Blue'
                    WHEN 'Duke' THEN 'Purple'
                    ELSE "Type" END;
                """);

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Players");

            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "Events",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
