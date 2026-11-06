using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlansV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeatureFloor",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LockedMonthlyPrice",
                table: "Tenants",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LockedYearlyPrice",
                table: "Tenants",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrialPlanId",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiAssistant",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Automations",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Documents",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxAiActionsPerMonth",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PrioritySupport",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SalesTools",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Surveys",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WhiteLabel",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Obecni klienci przed zmianą pakietów: cena zostaje (blokada), a uprawnienia nie spadają poniżej tego,
            // co mieli dotąd (podłoga) — łącznie z funkcjami, które do tej pory były w każdym planie.
            migrationBuilder.Sql("""
                UPDATE "Tenants" t SET "LockedMonthlyPrice" = p."MonthlyPrice", "LockedYearlyPrice" = p."YearlyPrice"
                FROM "Plans" p
                WHERE p."Id" = t."PlanId" AND p."MonthlyPrice" > 0 AND t."Status" <> 4; -- 4 = TenantStatus.Destroyed
                """);
            migrationBuilder.Sql("""
                UPDATE "Tenants" t SET "FeatureFloor" = ((to_jsonb(p)
                    - 'Id' - 'Name' - 'Description' - 'MonthlyPrice' - 'YearlyPrice' - 'Currency' - 'SortOrder' - 'IsActive' - 'IsFeatured'
                    - 'StripeProductId' - 'StripeMonthlyPriceId' - 'StripeYearlyPriceId' - 'TrialDays')
                    || '{"SalesTools":true,"Surveys":true,"Automations":true,"Documents":true,"AiAssistant":true,"MaxAiActionsPerMonth":300}'::jsonb)::text
                FROM "Plans" p
                WHERE p."Id" = t."PlanId" AND t."Status" <> 4; -- 4 = TenantStatus.Destroyed
                """);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                columns: new[] { "AiAssistant", "Automations", "CustomDomain", "Description", "Documents", "MaxAiActionsPerMonth", "MaxClients", "MaxCourses", "MaxSessionsPerMonth", "MaxSmsPerMonth", "MaxStorageGB", "MaxTrainers", "MonthlyPrice", "PrioritySupport", "SalesTools", "Surveys", "WhiteLabel", "YearlyPrice" },
                values: new object[] { true, true, true, "Wszystko, żeby klienci wracali i polecali Cię dalej", true, 300, 75, 5, 2147483647, 150, 20, 1, 119m, false, true, true, false, 1190m });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                columns: new[] { "AiAssistant", "Automations", "Description", "Documents", "MaxAiActionsPerMonth", "PrioritySupport", "PushNotifications", "SalesTools", "Surveys", "TrialDays", "WhiteLabel" },
                values: new object[] { false, false, "Za darmo — na początek 14 dni pełnego planu Pro", false, 0, false, true, false, false, 0, false });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "starter",
                columns: new[] { "AiAssistant", "Automations", "Description", "Documents", "IntegrationPrzelewy24", "MaxAiActionsPerMonth", "MaxClients", "MaxCourses", "MaxSessionsPerMonth", "MonthlyPrice", "Name", "PrioritySupport", "PushNotifications", "SalesTools", "Surveys", "TrainingPlansEnabled", "WhiteLabel", "YearlyPrice" },
                values: new object[] { false, false, "Trener, który przyjmuje klientów online", true, true, 0, 20, 0, 400, 59m, "Solo", false, true, false, true, true, false, 590m });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                columns: new[] { "AiAssistant", "Automations", "CustomDomain", "Description", "Documents", "MaxAiActionsPerMonth", "MaxSmsPerMonth", "MaxTrainers", "MonthlyPrice", "PrioritySupport", "SalesTools", "Surveys", "WhiteLabel", "YearlyPrice" },
                values: new object[] { true, true, true, "Studio z zespołem, Twoja marka i opieka wdrożeniowa", true, 1500, 750, 5, 249m, true, true, true, true, 2490m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeatureFloor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LockedMonthlyPrice",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LockedYearlyPrice",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialPlanId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AiAssistant",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Automations",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Documents",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxAiActionsPerMonth",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PrioritySupport",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SalesTools",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Surveys",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "WhiteLabel",
                table: "Plans");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                columns: new[] { "CustomDomain", "Description", "MaxClients", "MaxCourses", "MaxSessionsPerMonth", "MaxSmsPerMonth", "MaxStorageGB", "MaxTrainers", "MonthlyPrice", "YearlyPrice" },
                values: new object[] { false, "Pełna funkcjonalność dla aktywnych trenerów", 50, 10, 500, 50, 10, 2, 99m, 990m });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                columns: new[] { "Description", "PushNotifications", "TrialDays" },
                values: new object[] { "Testuj za darmo przez 7 dni", false, 7 });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "starter",
                columns: new[] { "Description", "IntegrationPrzelewy24", "MaxClients", "MaxCourses", "MaxSessionsPerMonth", "MonthlyPrice", "Name", "PushNotifications", "TrainingPlansEnabled", "YearlyPrice" },
                values: new object[] { "Podstawowe narzędzia dla trenera", false, 15, 3, 200, 49m, "Starter", false, false, 490m });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                columns: new[] { "CustomDomain", "Description", "MaxSmsPerMonth", "MaxTrainers", "MonthlyPrice", "YearlyPrice" },
                values: new object[] { false, "Bez limitów, pełna kontrola, priorytetowe wsparcie", 500, 10, 199m, 1990m });
        }
    }
}
