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

## Deployment (Docker / Portainer / Watchtower)

Production runs as a Docker image built from the root `Dockerfile` (multi-stage: .NET 10 SDK → aspnet runtime, adds `postgresql-client` for `BackupService` `pg_dump`/`psql`, runs as UID 10001, exposes `:8080`, has a curl-based healthcheck).

CI (`.github/workflows/build-image.yml`) builds and pushes to `ghcr.io/graba994/ptscheduler.web:latest` on push to `main`. Watchtower on the target VPS auto-pulls new tags (the app container has `com.centurylinklabs.watchtower.enable=true`).

`docker-compose.yml` is a Portainer-ready stack (Postgres 16 + app) with three volumes:

- `app-data` → `/app/data` — **DataProtection keys** live here (`data/keys/`). If you skip this volume, every deploy rotates keys and invalidates all auth cookies (logs everyone out).
- `app-uploads` → `/app/wwwroot/uploads` — branding logos etc.
- `db-data` → Postgres storage.

`Program.cs` applies migrations on startup (`db.Database.MigrateAsync()`) before seeding, so a fresh container against an empty DB just works — no manual `dotnet ef database update` needed in production.

Behind Nginx Proxy Manager: `UseForwardedHeaders` is wired to trust `X-Forwarded-For`/`X-Forwarded-Proto` from ANY proxy (`KnownNetworks`/`KnownProxies` cleared) — required so cookies and auth see the original HTTPS scheme.

## Roles and Authorization

Four roles are declared in `Domain.Constants.Roles`, but responsibilities split as:

- **Admin** — infra/ops: DB connection string (`/admin/settings`), backup/restore (`/admin/backup`). Attribute-gated with `Roles = Roles.Admin`.
- **Trainer** (Owner of a tenant instance) — business config: branding, users, clients, sessions, packages, intro config. Pages allow `"Admin,Trainer"`.
- **Subordinate** — assistant of a Trainer (linked via `ApplicationUser.SupervisorId`).
- **Client** — end user (kursantka).

`UserManagementService` enforces role scoping in code (defense in depth), not only via `[Authorize]`:

- `GetVisibleUsersAsync(callerRole)` hides Admin users from a Trainer caller.
- `GetAssignableRoles(callerRole)` returns what the caller may assign: Admin gets everything; Trainer gets only `Subordinate`/`Client`.
- `SetRoleAsync` / `SetLockoutAsync` / `DeleteUserAsync` all take `callerRole` and throw `UnauthorizedAccessException` if a non-Admin tries to modify an Admin or another Trainer.

When adding new admin/owner pages, decide upfront whether they're **infra** (Admin only) or **business** (`Admin,Trainer`) and gate accordingly, both on the page attribute AND in `NavMenu.razor` links.

## Learning Portal (Academy)

Course hierarchy `AcademyCourse → AcademyModule → AcademyLesson`, plus `AcademyEnrollment` (access grant per user×course, unique index on `(ApplicationUserId, CourseId)`, `ExpiresAt` + `IsRevoked`, computed `IsActive`) and `AcademyLessonProgress` (per user×lesson, unique index). Split into two services:

- `IAcademyCatalogService` — Owner-facing CRUD (courses/modules/lessons/enrollments). Owner pages under `Components/Pages/Academy/` gated `Admin,Trainer`: `/academy/courses`, `/academy/courses/{id}`, `/academy/lessons/{id}`, `/academy/enrollments`.
- `IAcademyStudentService` — student-facing; **every method takes `applicationUserId` and re-checks active enrollment itself** (never trust the page). Client pages gated `Client`: `/academy`, `/academy/{courseId}`, `/academy/lesson/{id}`.

Video is embedded via `<iframe>` and streams from the provider's CDN (Google Drive / Bunny / Vimeo), never through the VPS. `AcademyLesson.VideoProvider` (enum) + `VideoRef` (id/guid only, not a full URL). When rendering the iframe `src`, `VideoRef` is **charset-whitelisted** (`^[A-Za-z0-9_-]+$`, Bunny also allows `/`) so it can't inject a foreign origin — keep that guard if you touch the viewer. `AcademyCatalogService.NormalizeVideoRef` extracts the bare id from a pasted Google Drive URL on save.

Lesson text `Content` is rendered as `MarkupString` (raw HTML) — safe only because the author is a trusted Trainer in a single-tenant instance. If lesson content ever comes from an untrusted source, add HTML sanitization.

Hand-writing EF migrations: there is no `dotnet` in the dev container, so migrations here (e.g. `20260717120000_AddAcademy`) are authored by hand — migration file + `.Designer.cs` + updated `ApplicationDbContextModelSnapshot.cs`. Runtime `MigrateAsync()` only runs `Up()`, but keep the snapshot correct so the next real `dotnet ef` diff is clean.

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
