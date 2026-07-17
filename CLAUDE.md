# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Build / run from the repo root (solution file is `PTScheduler.Web.slnx`):

```bash
dotnet build PTScheduler.Web.slnx
dotnet run --project PTScheduler.Web
```

EF Core migrations are owned by the `PTScheduler.Infrastructure` project, but the startup project is `PTScheduler.Web`. Always specify both:

```bash
dotnet ef migrations add <Name> --project PTScheduler.Infrastructure --startup-project PTScheduler.Web
dotnet ef database update     --project PTScheduler.Infrastructure --startup-project PTScheduler.Web
```

The design-time factory (`ApplicationDbContextFactory`) reads the `PTSCHEDULER_CONN` env var and falls back to a local Postgres on `ptscheduler_dev`. At runtime the connection string comes from `ConnectionStrings:DefaultConnection` in `appsettings.json` or `connections.json` (see Configuration below).

There is currently no test project in the solution.

## Architecture

Clean / layered architecture targeting **.NET 10**, with four projects wired through the `slnx` solution:

- `PTScheduler.Domain` — entities (`Client`, `Session`, `SessionPackage`, `BodyMeasurement`, `TrainerNote`, `IntroSessionConfig`, `SessionType`, `AppBranding`), enums, and the `Roles` constants (`Admin`, `Trainer`, `Subordinate`, `Client`). No dependencies.
- `PTScheduler.Application` — DTOs and service **interfaces** only (`IClientService`, `ISessionService`, `ISessionPackageService`, `IIntroSessionService`, `IBrandingService`, `IBackupService`, `IUserManagementService`, `IDatabaseMaintenanceService`, `IDatabaseSettingsService`, `IWebRootPathProvider`). `AddApplication()` is currently a no-op extension — register new application contracts here when implementations move.
- `PTScheduler.Infrastructure` — EF Core (`ApplicationDbContext`, Npgsql/PostgreSQL), migrations, `DbInitializer` (role + session-type seeding), and the concrete service implementations. All services are registered scoped via `AddInfrastructure(configuration, contentRootPath)`.
- `PTScheduler.Web` — ASP.NET Core host using **Blazor Server** (`AddInteractiveServerComponents`, `AddInteractiveServerRenderMode`). Razor components live under `Components/{Pages,Layout,Account}`, with feature folders `Pages/{Clients,Admin,Trainer,Dashboard}`. Auth uses **ASP.NET Core Identity** with `ApplicationUser` (defined in Infrastructure) stored in the same `ApplicationDbContext`, cookie scheme, `RequireConfirmedAccount = true`, role support, and a no-op `IEmailSender`.

Cross-cutting points to know:

- `Program.cs` calls `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` — keep this if you touch DateTime columns, otherwise Npgsql will reject non-UTC values.
- `BlazorDisableThrowNavigationException` is on in the csproj.
- Roles are seeded on startup via `DbInitializer.SeedRolesAsync` and session types via `SeedSessionTypesAsync` — both run inside a scope right after `app.Build()`.
- Admin-only minimal endpoint `GET /admin/backup/download` is mapped directly in `Program.cs` and gated by `ctx.User.IsInRole(Roles.Admin)` + `RequireAuthorization()`. Add similar admin endpoints the same way rather than via controllers.
- `IWebRootPathProvider` is registered as a singleton in `Program.cs` (not in `AddInfrastructure`) because the implementation lives in the Web project.

## Configuration

`Program.cs` adds an extra optional file `connections.json` at the content root, layered on top of `appsettings.json`. `DatabaseSettingsService.SaveConnectionString` **writes to that file** at runtime (admin UI), so don't move connection-string handling back into `appsettings.json` or that flow breaks. The file path is passed explicitly into `AddInfrastructure(configuration, contentRootPath)`.

The default connection string in `appsettings.json` is a placeholder — real local dev should set `connections.json` or override via user secrets (`UserSecretsId` is set on the Web project).
