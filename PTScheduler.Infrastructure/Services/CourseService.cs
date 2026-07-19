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

    private static void Apply(Course c, SaveCourseDto dto)
    {
        c.Title = dto.Title.Trim();
        c.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
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
