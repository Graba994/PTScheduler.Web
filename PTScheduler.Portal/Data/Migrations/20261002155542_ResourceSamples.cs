using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ResourceSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CpuCores = table.Column<double>(type: "double precision", nullable: true),
                    CpuLimitCores = table.Column<double>(type: "double precision", nullable: true),
                    MemoryBytes = table.Column<long>(type: "bigint", nullable: true),
                    MemoryLimitBytes = table.Column<long>(type: "bigint", nullable: true),
                    WebMemoryBytes = table.Column<long>(type: "bigint", nullable: true),
                    DbMemoryBytes = table.Column<long>(type: "bigint", nullable: true),
                    DbSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    FilesBytes = table.Column<long>(type: "bigint", nullable: true),
                    DiskUsedBytes = table.Column<long>(type: "bigint", nullable: true),
                    DiskTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    BackupDiskUsedBytes = table.Column<long>(type: "bigint", nullable: true),
                    BackupDiskTotalBytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSamples_At",
                table: "ResourceSamples",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceSamples_TenantId_At",
                table: "ResourceSamples",
                columns: new[] { "TenantId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceSamples");
        }
    }
}
