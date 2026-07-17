using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAcademy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcademyCourses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CoverImageUrl = table.Column<string>(type: "text", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultAccessDays = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyCourses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AcademyModules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcademyModules_AcademyCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "AcademyCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AcademyEnrollments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "text", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcademyEnrollments_AcademyCourses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "AcademyCourses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AcademyLessons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    VideoProvider = table.Column<int>(type: "integer", nullable: false),
                    VideoRef = table.Column<string>(type: "text", nullable: true),
                    WorkbookUrl = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyLessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcademyLessons_AcademyModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "AcademyModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AcademyLessonProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LessonId = table.Column<int>(type: "integer", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "text", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademyLessonProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcademyLessonProgress_AcademyLessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "AcademyLessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademyEnrollments_ApplicationUserId_CourseId",
                table: "AcademyEnrollments",
                columns: new[] { "ApplicationUserId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyEnrollments_CourseId",
                table: "AcademyEnrollments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyLessonProgress_ApplicationUserId_LessonId",
                table: "AcademyLessonProgress",
                columns: new[] { "ApplicationUserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademyLessonProgress_LessonId",
                table: "AcademyLessonProgress",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyLessons_ModuleId",
                table: "AcademyLessons",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AcademyModules_CourseId",
                table: "AcademyModules",
                column: "CourseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcademyEnrollments");

            migrationBuilder.DropTable(
                name: "AcademyLessonProgress");

            migrationBuilder.DropTable(
                name: "AcademyLessons");

            migrationBuilder.DropTable(
                name: "AcademyModules");

            migrationBuilder.DropTable(
                name: "AcademyCourses");
        }
    }
}
