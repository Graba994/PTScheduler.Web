using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class CourseService(IDbContextFactory<ApplicationDbContext> dbFactory) : ICourseService
{
    public async Task<List<CourseDto>> GetCoursesAsync(bool includeUnpublished = true)
    {
        var now = DateTime.UtcNow;
        await using var db = dbFactory.CreateDbContext();
        var q = db.Courses.AsNoTracking();
        if (!includeUnpublished) q = q.Where(c => c.IsPublished);
        return await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new CourseDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                DescriptionHtml = c.DescriptionHtml,
                CoverImageUrl = c.CoverImageUrl,
                IsPublished = c.IsPublished,
                Price = c.Price,
                DefaultAccessType = c.DefaultAccessType,
                DefaultAccessDays = c.DefaultAccessDays,
                SortOrder = c.SortOrder,
                CreatedAt = c.CreatedAt,
                EnrollmentCount = c.Enrollments.Count,
                ActiveEnrollmentCount = c.Enrollments.Count(e =>
                    !e.IsRevoked
                    && (e.ExpiresAt == null || e.ExpiresAt > now)
                    && (e.StartsAt == null || e.StartsAt <= now))
            })
            .ToListAsync();
    }

    public async Task<CourseDto?> GetCourseAsync(int id)
    {
        var now = DateTime.UtcNow;
        await using var db = dbFactory.CreateDbContext();
        return await db.Courses.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CourseDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                DescriptionHtml = c.DescriptionHtml,
                CoverImageUrl = c.CoverImageUrl,
                IsPublished = c.IsPublished,
                Price = c.Price,
                DefaultAccessType = c.DefaultAccessType,
                DefaultAccessDays = c.DefaultAccessDays,
                SortOrder = c.SortOrder,
                CreatedAt = c.CreatedAt,
                EnrollmentCount = c.Enrollments.Count,
                ActiveEnrollmentCount = c.Enrollments.Count(e =>
                    !e.IsRevoked
                    && (e.ExpiresAt == null || e.ExpiresAt > now)
                    && (e.StartsAt == null || e.StartsAt <= now))
            })
            .FirstOrDefaultAsync();
    }

    public async Task<int> CreateCourseAsync(SaveCourseDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = new Course();
        Apply(c, dto);
        c.CreatedAt = DateTime.UtcNow;
        db.Courses.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    public async Task UpdateCourseAsync(int id, SaveCourseDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return;
        Apply(c, dto);
        await db.SaveChangesAsync();
    }

    public async Task DeleteCourseAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return;
        db.Courses.Remove(c);
        await db.SaveChangesAsync();
    }

    public async Task<List<CourseEnrollmentDto>> GetEnrollmentsAsync(int courseId)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await (from e in db.CourseEnrollments.AsNoTracking()
                          where e.CourseId == courseId
                          join u in db.Users.AsNoTracking() on e.ApplicationUserId equals u.Id into gj
                          from u in gj.DefaultIfEmpty()
                          orderby e.GrantedAt descending
                          select new EnrollmentRow
                          {
                              Id = e.Id,
                              CourseId = e.CourseId,
                              CourseTitle = e.Course.Title,
                              ApplicationUserId = e.ApplicationUserId,
                              Email = u!.Email,
                              FirstName = u.FirstName,
                              LastName = u.LastName,
                              AccessType = e.AccessType,
                              Source = e.Source,
                              GrantedAt = e.GrantedAt,
                              StartsAt = e.StartsAt,
                              ExpiresAt = e.ExpiresAt,
                              IsRevoked = e.IsRevoked,
                              Notes = e.Notes
                          }).ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<List<CourseEnrollmentDto>> GetUserEnrollmentsAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await (from e in db.CourseEnrollments.AsNoTracking()
                          where e.ApplicationUserId == userId
                          orderby e.GrantedAt descending
                          select new EnrollmentRow
                          {
                              Id = e.Id,
                              CourseId = e.CourseId,
                              CourseTitle = e.Course.Title,
                              ApplicationUserId = e.ApplicationUserId,
                              AccessType = e.AccessType,
                              Source = e.Source,
                              GrantedAt = e.GrantedAt,
                              StartsAt = e.StartsAt,
                              ExpiresAt = e.ExpiresAt,
                              IsRevoked = e.IsRevoked,
                              Notes = e.Notes
                          }).ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<string?> GrantAccessAsync(GrantEnrollmentDto dto, string grantedByUserId)
    {
        await using var db = dbFactory.CreateDbContext();

        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == dto.CourseId);
        if (course is null) return "Kurs nie istnieje.";

        var userExists = await db.Users.AnyAsync(u => u.Id == dto.ApplicationUserId);
        if (!userExists) return "Wybrany użytkownik nie istnieje.";

        DateTime? expires = dto.AccessType == CourseAccessType.Lifetime
            ? null
            : dto.ExpiresAt ?? (dto.AccessDays.HasValue ? DateTime.UtcNow.AddDays(dto.AccessDays.Value) : null);

        db.CourseEnrollments.Add(new CourseEnrollment
        {
            CourseId = dto.CourseId,
            ApplicationUserId = dto.ApplicationUserId,
            AccessType = dto.AccessType,
            Source = EnrollmentSource.Manual,
            GrantedAt = DateTime.UtcNow,
            ExpiresAt = expires,
            GrantedByUserId = grantedByUserId,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        });
        await db.SaveChangesAsync();
        return null;
    }

    public async Task RevokeAccessAsync(int enrollmentId) => await SetRevoked(enrollmentId, true);
    public async Task RestoreAccessAsync(int enrollmentId) => await SetRevoked(enrollmentId, false);

    public async Task<bool> HasActiveAccessAsync(string userId, int courseId)
    {
        var now = DateTime.UtcNow;
        await using var db = dbFactory.CreateDbContext();
        return await db.CourseEnrollments.AsNoTracking().AnyAsync(e =>
            e.ApplicationUserId == userId
            && e.CourseId == courseId
            && !e.IsRevoked
            && (e.ExpiresAt == null || e.ExpiresAt > now)
            && (e.StartsAt == null || e.StartsAt <= now));
    }

    private async Task SetRevoked(int enrollmentId, bool revoked)
    {
        await using var db = dbFactory.CreateDbContext();
        var e = await db.CourseEnrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId);
        if (e is null) return;
        e.IsRevoked = revoked;
        await db.SaveChangesAsync();
    }

    // ---- Course content: modules + lessons ----

    public async Task<List<ModuleDto>> GetContentAsync(int courseId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.CourseModules.AsNoTracking()
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new ModuleDto
            {
                Id = m.Id,
                CourseId = m.CourseId,
                Title = m.Title,
                SortOrder = m.SortOrder,
                Lessons = m.Lessons
                    .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                    .Select(l => new LessonDto
                    {
                        Id = l.Id,
                        ModuleId = l.ModuleId,
                        Title = l.Title,
                        SortOrder = l.SortOrder,
                        VideoUrl = l.VideoUrl,
                        ContentHtml = l.ContentHtml
                    }).ToList()
            })
            .ToListAsync();
    }

    public async Task<int> AddModuleAsync(int courseId, string title)
    {
        await using var db = dbFactory.CreateDbContext();
        var maxOrder = await db.CourseModules
            .Where(m => m.CourseId == courseId)
            .Select(m => (int?)m.SortOrder).MaxAsync() ?? -1;
        var m = new CourseModule
        {
            CourseId = courseId,
            Title = string.IsNullOrWhiteSpace(title) ? "Nowy moduł" : title.Trim(),
            SortOrder = maxOrder + 1
        };
        db.CourseModules.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    public async Task UpdateModuleAsync(int moduleId, string title)
    {
        await using var db = dbFactory.CreateDbContext();
        var m = await db.CourseModules.FirstOrDefaultAsync(x => x.Id == moduleId);
        if (m is null) return;
        m.Title = string.IsNullOrWhiteSpace(title) ? m.Title : title.Trim();
        await db.SaveChangesAsync();
    }

    public async Task DeleteModuleAsync(int moduleId)
    {
        await using var db = dbFactory.CreateDbContext();
        var m = await db.CourseModules.FirstOrDefaultAsync(x => x.Id == moduleId);
        if (m is null) return;
        db.CourseModules.Remove(m);
        await db.SaveChangesAsync();
    }

    public async Task MoveModuleAsync(int moduleId, int direction)
    {
        await using var db = dbFactory.CreateDbContext();
        var m = await db.CourseModules.FirstOrDefaultAsync(x => x.Id == moduleId);
        if (m is null) return;
        var siblings = await db.CourseModules
            .Where(x => x.CourseId == m.CourseId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync();
        Reorder(siblings, moduleId, direction, x => x.Id, (x, o) => x.SortOrder = o);
        await db.SaveChangesAsync();
    }

    public async Task<int> AddLessonAsync(int moduleId, SaveLessonDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var maxOrder = await db.Lessons
            .Where(l => l.ModuleId == moduleId)
            .Select(l => (int?)l.SortOrder).MaxAsync() ?? -1;
        var lesson = new Lesson
        {
            ModuleId = moduleId,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? "Nowa lekcja" : dto.Title.Trim(),
            VideoUrl = string.IsNullOrWhiteSpace(dto.VideoUrl) ? null : dto.VideoUrl.Trim(),
            ContentHtml = string.IsNullOrWhiteSpace(dto.ContentHtml) ? null : dto.ContentHtml,
            SortOrder = maxOrder + 1
        };
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        return lesson.Id;
    }

    public async Task UpdateLessonAsync(int lessonId, SaveLessonDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId);
        if (l is null) return;
        l.Title = string.IsNullOrWhiteSpace(dto.Title) ? l.Title : dto.Title.Trim();
        l.VideoUrl = string.IsNullOrWhiteSpace(dto.VideoUrl) ? null : dto.VideoUrl.Trim();
        l.ContentHtml = string.IsNullOrWhiteSpace(dto.ContentHtml) ? null : dto.ContentHtml;
        await db.SaveChangesAsync();
    }

    public async Task DeleteLessonAsync(int lessonId)
    {
        await using var db = dbFactory.CreateDbContext();
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId);
        if (l is null) return;
        db.Lessons.Remove(l);
        await db.SaveChangesAsync();
    }

    public async Task MoveLessonAsync(int lessonId, int direction)
    {
        await using var db = dbFactory.CreateDbContext();
        var l = await db.Lessons.FirstOrDefaultAsync(x => x.Id == lessonId);
        if (l is null) return;
        var siblings = await db.Lessons
            .Where(x => x.ModuleId == l.ModuleId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync();
        Reorder(siblings, lessonId, direction, x => x.Id, (x, o) => x.SortOrder = o);
        await db.SaveChangesAsync();
    }

    // Moves the item with the given id up (-1) or down (+1) in the list and
    // renormalizes SortOrder to a clean 0..n-1 sequence.
    private static void Reorder<T>(List<T> items, int id, int direction, Func<T, int> idOf, Action<T, int> setOrder)
    {
        var idx = items.FindIndex(x => idOf(x) == id);
        if (idx < 0) return;
        var target = idx + direction;
        if (target < 0 || target >= items.Count) return;
        var item = items[idx];
        items.RemoveAt(idx);
        items.Insert(target, item);
        for (int i = 0; i < items.Count; i++) setOrder(items[i], i);
    }

    private static void Apply(Course c, SaveCourseDto dto)
    {
        c.Title = dto.Title.Trim();
        c.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        c.DescriptionHtml = string.IsNullOrWhiteSpace(dto.DescriptionHtml) ? null : dto.DescriptionHtml;
        c.CoverImageUrl = string.IsNullOrWhiteSpace(dto.CoverImageUrl) ? null : dto.CoverImageUrl.Trim();
        c.IsPublished = dto.IsPublished;
        c.Price = dto.Price < 0 ? 0 : dto.Price;
        c.DefaultAccessType = dto.DefaultAccessType;
        c.DefaultAccessDays = dto.DefaultAccessType == CourseAccessType.Lifetime ? null : dto.DefaultAccessDays;
        c.SortOrder = dto.SortOrder;
    }

    private static CourseEnrollmentDto Map(EnrollmentRow r)
    {
        var now = DateTime.UtcNow;
        return new CourseEnrollmentDto
        {
            Id = r.Id,
            CourseId = r.CourseId,
            CourseTitle = r.CourseTitle,
            ApplicationUserId = r.ApplicationUserId,
            UserEmail = r.Email ?? string.Empty,
            UserName = $"{r.FirstName} {r.LastName}".Trim(),
            AccessType = r.AccessType,
            Source = r.Source,
            GrantedAt = r.GrantedAt,
            StartsAt = r.StartsAt,
            ExpiresAt = r.ExpiresAt,
            IsRevoked = r.IsRevoked,
            IsActive = !r.IsRevoked
                       && (r.ExpiresAt == null || r.ExpiresAt > now)
                       && (r.StartsAt == null || r.StartsAt <= now),
            Notes = r.Notes
        };
    }

    // Flat projection row used to keep the EF query translatable; mapped to the DTO in memory.
    private sealed class EnrollmentRow
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public string CourseTitle { get; set; } = string.Empty;
        public string ApplicationUserId { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public CourseAccessType AccessType { get; set; }
        public EnrollmentSource Source { get; set; }
        public DateTime GrantedAt { get; set; }
        public DateTime? StartsAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsRevoked { get; set; }
        public string? Notes { get; set; }
    }
}
