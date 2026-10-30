using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>API aplikacji trenera: wideo w centralnej bibliotece Bunny i Społeczność ćwiczeń (nagłówek X-Internal-Secret).</summary>
public static class VideoHubEndpoints
{
    public sealed record CreateVideoRequest(string? Title, string? Kind, long SizeBytes);
    public sealed record CopyRequest(int Id);
    public sealed record DeleteImageRequest(string? Url);
    public sealed record EmbedRequest(string[]? Refs);

    public static void MapVideoHub(this WebApplication app)
    {
        var g = app.MapGroup("/api/internal/tenants/{slug}");

        g.MapGet("/video", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, VideoHubService hub, ImageHubService images) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
            {
                var s = await hub.StatusAsync(t);
                return Results.Json(new
                {
                    enabled = s.Enabled, reason = s.Reason, libraryId = s.LibraryId, cdnHostname = s.CdnHostname,
                    quotaBytes = s.QuotaBytes, usedBytes = s.UsedBytes, videos = s.Videos, legacyLibraryId = s.LegacyLibraryId,
                    imagesEnabled = s.QuotaBytes > 0 && await images.EnabledAsync()
                });
            }));

        // Zdjęcie (już zmniejszone w aplikacji) — surowe bajty w treści, rodzaj w zapytaniu. Ten sam limit co filmy.
        g.MapPost("/images", async (string slug, string? kind, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, ImageHubService images) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
            {
                if (ctx.Request.ContentLength is > ImageHubService.MaxBytes)
                    return Results.Json(new { error = "Zdjęcie jest za duże (najwięcej 10 MB)." }, statusCode: StatusCodes.Status413PayloadTooLarge);
                using var buffer = new MemoryStream();
                await ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted);
                var (url, size, error) = await images.UploadAsync(t, kind ?? VideoKinds.Exercises, buffer.ToArray(), ctx.RequestAborted);
                return url is null
                    ? Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict)
                    : Results.Json(new { url, sizeBytes = size });
            }));

        g.MapPost("/images/delete", async (string slug, DeleteImageRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, ImageHubService images) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
                !string.IsNullOrWhiteSpace(body.Url) && await images.DeleteAsync(t, body.Url) ? Results.Ok() : Results.NotFound()));

        // Podpisane (czasowe) linki do odtwarzacza — klucz tokenów zostaje w Portalu.
        g.MapPost("/video/embed", async (string slug, EmbedRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, VideoHubService hub) =>
            await WithTenant(slug, ctx, dbf, config, async _ =>
            {
                var refs = (body.Refs ?? []).Where(r => !string.IsNullOrWhiteSpace(r) && r.Length <= 140).Take(50).ToList();
                var (urls, expires) = await hub.SignEmbedsAsync(refs, DateTimeOffset.UtcNow);
                return Results.Json(new { urls, expires });
            }));

        g.MapGet("/video/list", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, VideoHubService hub) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
                Results.Json((await hub.ListAsync(t)).Select(v => new { videoId = v.VideoGuid, kind = v.Kind, title = v.Title, sizeBytes = v.SizeBytes, createdAt = v.CreatedAt }))));

        g.MapPost("/video", async (string slug, CreateVideoRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, VideoHubService hub) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
            {
                var (upload, error) = await hub.CreateAsync(t, body.Kind ?? VideoKinds.Courses, body.Title ?? "", body.SizeBytes, ctx.RequestAborted);
                return upload is null
                    ? Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict)
                    : Results.Json(new { videoId = upload.VideoId, libraryId = upload.LibraryId, signature = upload.Signature, expires = upload.Expires });
            }));

        g.MapGet("/video/{guid}", async (string slug, string guid, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, VideoHubService hub) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
                await hub.InfoAsync(t, guid) is { } i
                    ? Results.Json(new { videoId = i.VideoId, title = i.Title, sizeBytes = i.SizeBytes, length = i.LengthSeconds, status = i.State })
                    : Results.NotFound()));

        g.MapDelete("/video/{guid}", async (string slug, string guid, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, VideoHubService hub) =>
            await WithTenant(slug, ctx, dbf, config, async t => await hub.DeleteAsync(t, guid) ? Results.Ok() : Results.NotFound()));

        // RODO: wszystkie dane wideo trenera (filmy, foldery, udostępnione ćwiczenia).
        g.MapDelete("/video", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, VideoHubService hub, ImageHubService images) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
            {
                var removed = await hub.DeleteAllAsync(t);
                return Results.Json(new { removed, images = await images.DeleteAllAsync(t) });
            }));

        g.MapGet("/community/exercises", async (string slug, string? q, string? muscle, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, CommunityExerciseService community) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
            {
                if (!await community.AllowedAsync(t)) return Results.Json(new { allowed = false, items = Array.Empty<object>(), shared = Array.Empty<int>() });
                return Results.Json(new { allowed = true, items = await community.SearchAsync(t, q, muscle), shared = await community.SharedIdsAsync(t) });
            }));

        g.MapPost("/community/exercises", async (string slug, CommunityExerciseService.ShareInput body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, CommunityExerciseService community) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
            {
                var (ok, error, id) = await community.ShareAsync(t, body);
                return ok ? Results.Json(new { id }) : Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);
            }));

        g.MapDelete("/community/exercises/{sourceId:int}", async (string slug, int sourceId, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, CommunityExerciseService community) =>
            await WithTenant(slug, ctx, dbf, config, async t => await community.UnshareAsync(t, sourceId) ? Results.Ok() : Results.NotFound()));

        g.MapPost("/community/exercises/copy", async (string slug, CopyRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, CommunityExerciseService community) =>
            await WithTenant(slug, ctx, dbf, config, async t =>
            {
                if (!await community.AllowedAsync(t)) return Results.Json(new { error = "Społeczność jest dostępna w pakiecie z hostingiem wideo." }, statusCode: StatusCodes.Status409Conflict);
                return await community.CopyAsync(t, body.Id) is { } item ? Results.Json(item) : Results.NotFound();
            }));
    }

    private static async Task<IResult> WithTenant(string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbf, IConfiguration config, Func<Tenant, Task<IResult>> action)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
        return await action(tenant);
    }
}
