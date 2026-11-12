using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerTrainerUserId = table.Column<string>(type: "text", nullable: true),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    NamePl = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    DescriptionPl = table.Column<string>(type: "text", nullable: true),
                    DescriptionEn = table.Column<string>(type: "text", nullable: true),
                    PrimaryMuscles = table.Column<string>(type: "text", nullable: false),
                    SecondaryMuscles = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Equipment = table.Column<string>(type: "text", nullable: true),
                    Force = table.Column<string>(type: "text", nullable: true),
                    Mechanic = table.Column<string>(type: "text", nullable: true),
                    ImageUrls = table.Column<string>(type: "text", nullable: false),
                    VideoType = table.Column<int>(type: "integer", nullable: false),
                    VideoRef = table.Column<string>(type: "text", nullable: true),
                    SourceKey = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainerExercisePrefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    ExerciseId = table.Column<int>(type: "integer", nullable: false),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerExercisePrefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainerExercisePrefs_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrainingPlans_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlanDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanDays_TrainingPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TrainingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanExercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanDayId = table.Column<int>(type: "integer", nullable: false),
                    ExerciseId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Sets = table.Column<int>(type: "integer", nullable: false),
                    Reps = table.Column<string>(type: "text", nullable: true),
                    TargetWeightKg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    Tempo = table.Column<string>(type: "text", nullable: true),
                    RestSeconds = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanExercises_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanExercises_PlanDays_PlanDayId",
                        column: x => x.PlanDayId,
                        principalTable: "PlanDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    PlanExerciseId = table.Column<int>(type: "integer", nullable: true),
                    ExerciseId = table.Column<int>(type: "integer", nullable: false),
                    WorkoutDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_PlanExercises_PlanExerciseId",
                        column: x => x.PlanExerciseId,
                        principalTable: "PlanExercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSetLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkoutLogId = table.Column<int>(type: "integer", nullable: false),
                    SetNumber = table.Column<int>(type: "integer", nullable: false),
                    Reps = table.Column<int>(type: "integer", nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSetLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutSetLogs_WorkoutLogs_WorkoutLogId",
                        column: x => x.WorkoutLogId,
                        principalTable: "WorkoutLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_OwnerTrainerUserId",
                table: "Exercises",
                column: "OwnerTrainerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_SourceKey",
                table: "Exercises",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_Visibility_Category",
                table: "Exercises",
                columns: new[] { "Visibility", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanDays_PlanId",
                table: "PlanDays",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanExercises_ExerciseId",
                table: "PlanExercises",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanExercises_PlanDayId",
                table: "PlanExercises",
                column: "PlanDayId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerExercisePrefs_ExerciseId",
                table: "TrainerExercisePrefs",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerExercisePrefs_TrainerUserId_ExerciseId",
                table: "TrainerExercisePrefs",
                columns: new[] { "TrainerUserId", "ExerciseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_ClientId",
                table: "TrainingPlans",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_TrainerUserId",
                table: "TrainingPlans",
                column: "TrainerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_ClientId_WorkoutDate",
                table: "WorkoutLogs",
                columns: new[] { "ClientId", "WorkoutDate" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_ExerciseId",
                table: "WorkoutLogs",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_PlanExerciseId",
                table: "WorkoutLogs",
                column: "PlanExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSetLogs_WorkoutLogId",
                table: "WorkoutSetLogs",
                column: "WorkoutLogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainerExercisePrefs");

            migrationBuilder.DropTable(
                name: "WorkoutSetLogs");

            migrationBuilder.DropTable(
                name: "WorkoutLogs");

            migrationBuilder.DropTable(
                name: "PlanExercises");

            migrationBuilder.DropTable(
                name: "PlanDays");

            migrationBuilder.DropTable(
                name: "TrainingPlans");

            migrationBuilder.DropTable(
                name: "Exercises");
        }
    }
}
