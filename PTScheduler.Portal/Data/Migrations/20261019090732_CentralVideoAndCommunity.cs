using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class CentralVideoAndCommunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommunityExercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    SourceExerciseId = table.Column<int>(type: "integer", nullable: false),
                    NamePl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PrimaryMuscles = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SecondaryMuscles = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Equipment = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Force = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Mechanic = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Tracking = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ImageUrls = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    VideoType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VideoRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConsentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    Copies = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityExercises_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantVideoFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CollectionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantVideoFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantVideos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    VideoGuid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantVideos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityExercises_Hidden_NamePl",
                table: "CommunityExercises",
                columns: new[] { "Hidden", "NamePl" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityExercises_TenantId_SourceExerciseId",
                table: "CommunityExercises",
                columns: new[] { "TenantId", "SourceExerciseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantVideoFolders_TenantId_Kind",
                table: "TenantVideoFolders",
                columns: new[] { "TenantId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantVideos_TenantId_Kind",
                table: "TenantVideos",
                columns: new[] { "TenantId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantVideos_VideoGuid",
                table: "TenantVideos",
                column: "VideoGuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunityExercises");

            migrationBuilder.DropTable(
                name: "TenantVideoFolders");

            migrationBuilder.DropTable(
                name: "TenantVideos");
        }
    }
}
