using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrainerStyleDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrainerStyleExamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerStyleExamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainerStyles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Length = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Greeting = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Signature = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerStyles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerStyleExamples_TrainerUserId_CreatedAt",
                table: "TrainerStyleExamples",
                columns: new[] { "TrainerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrainerStyles_TrainerUserId",
                table: "TrainerStyles",
                column: "TrainerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainerStyleExamples");

            migrationBuilder.DropTable(
                name: "TrainerStyles");
        }
    }
}
