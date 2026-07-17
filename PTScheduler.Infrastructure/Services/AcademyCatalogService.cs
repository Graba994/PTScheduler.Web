using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class AcademyCatalogService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager) : IAcademyCatalogService
{
    // ---- Courses ----

    public async Task<List<AcademyCourseDto>> GetCoursesAsync()
    {
        var courses = await db.AcademyCourses
            .Include(c => c.Modules).ThenInclude(m => m.Lessons)
            .Include(c => c.Enrollments)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync();

        return courses.Select(c => new AcademyCourseDto
        {
            Id = c.Id,
            Title = c.Title,
            Description = c.Description,
            CoverImageUrl = c.CoverImageUrl,
            IsPublished = c.IsPublished,
            DefaultAccessDays = c.DefaultAccessDays,
            SortOrder = c.SortOrder,
            ModuleCount = c.Modules.Count,
            LessonCount = c.Modules.Sum(m => m.Lessons.Count),
            EnrollmentCount = c.Enrollments.Count(e => !e.IsRevoked)
        }).ToList();
    }

    public async Task<AcademyCourseDto?> GetCourseAsync(int courseId)
    {
        var c = await db.AcademyCourses
            .Include(c => c.Modules).ThenInclude(m => m.Lessons)
            .Include(c => c.Enrollments)
            .FirstOrDefaultAsync(c => c.Id == courseId);

        if (c is null) return null;

        return new AcademyCourseDto
        {
            Id = c.Id,
            Title = c.Title,
            Description = c.Description,
            CoverImageUrl = c.CoverImageUrl,
            IsPublished = c.IsPublished,
            DefaultAccessDays = c.DefaultAccessDays,
            SortOrder = c.SortOrder,
            ModuleCount = c.Modules.Count,
            LessonCount = c.Modules.Sum(m => m.Lessons.Count),
            EnrollmentCount = c.Enrollments.Count(e => !e.IsRevoked)
        };
    }

    public async Task<int> CreateCourseAsync(AcademyCourseDto dto)
    {
        var course = new AcademyCourse
        {
            Title = dto.Title,
            Description = dto.Description,
            CoverImageUrl = dto.CoverImageUrl,
            IsPublished = dto.IsPublished,
            DefaultAccessDays = dto.DefaultAccessDays,
            SortOrder = dto.SortOrder
        };
        db.AcademyCourses.Add(course);
        await db.SaveChangesAsync();
        return course.Id;
    }

    public async Task UpdateCourseAsync(AcademyCourseDto dto)
    {
        var course = await db.AcademyCourses.FindAsync(dto.Id)
            ?? throw new InvalidOperationException("Kurs nie istnieje.");
        course.Title = dto.Title;
        course.Description = dto.Description;
        course.CoverImageUrl = dto.CoverImageUrl;
        course.IsPublished = dto.IsPublished;
        course.DefaultAccessDays = dto.DefaultAccessDays;
        course.SortOrder = dto.SortOrder;
        await db.SaveChangesAsync();
    }

    public async Task DeleteCourseAsync(int courseId)
    {
        var course = await db.AcademyCourses.FindAsync(courseId);
        if (course is null) return;
        db.AcademyCourses.Remove(course);
        await db.SaveChangesAsync();
    }

    // ---- Modules ----

    public async Task<List<AcademyModuleDto>> GetModulesAsync(int courseId)
    {
        var modules = await db.AcademyModules
            .Include(m => m.Lessons)
            .Where(m => m.CourseId == courseId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .ToListAsync();

        return modules.Select(m => new AcademyModuleDto
        {
            Id = m.Id,
            CourseId = m.CourseId,
            Title = m.Title,
            Description = m.Description,
            SortOrder = m.SortOrder,
            Lessons = m.Lessons
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                .Select(MapLesson).ToList()
        }).ToList();
    }

    public async Task<int> CreateModuleAsync(AcademyModuleDto dto)
    {
        var module = new AcademyModule
        {
            CourseId = dto.CourseId,
            Title = dto.Title,
            Description = dto.Description,
            SortOrder = dto.SortOrder
        };
        db.AcademyModules.Add(module);
        await db.SaveChangesAsync();
        return module.Id;
    }

    public async Task UpdateModuleAsync(AcademyModuleDto dto)
    {
        var module = await db.AcademyModules.FindAsync(dto.Id)
            ?? throw new InvalidOperationException("Moduł nie istnieje.");
        module.Title = dto.Title;
        module.Description = dto.Description;
        module.SortOrder = dto.SortOrder;
        await db.SaveChangesAsync();
    }

    public async Task DeleteModuleAsync(int moduleId)
    {
        var module = await db.AcademyModules.FindAsync(moduleId);
        if (module is null) return;
        db.AcademyModules.Remove(module);
        await db.SaveChangesAsync();
    }

    // ---- Lessons ----

    public async Task<AcademyLessonDto?> GetLessonAsync(int lessonId)
    {
        var lesson = await db.AcademyLessons.FindAsync(lessonId);
        return lesson is null ? null : MapLesson(lesson);
    }

    public async Task<int> CreateLessonAsync(AcademyLessonDto dto)
    {
        var lesson = new AcademyLesson
        {
            ModuleId = dto.ModuleId,
            Title = dto.Title,
            Content = dto.Content,
            VideoProvider = dto.VideoProvider,
            VideoRef = NormalizeVideoRef(dto.VideoProvider, dto.VideoRef),
            WorkbookUrl = dto.WorkbookUrl,
            SortOrder = dto.SortOrder
        };
        db.AcademyLessons.Add(lesson);
        await db.SaveChangesAsync();
        return lesson.Id;
    }

    public async Task UpdateLessonAsync(AcademyLessonDto dto)
    {
        var lesson = await db.AcademyLessons.FindAsync(dto.Id)
            ?? throw new InvalidOperationException("Lekcja nie istnieje.");
        lesson.Title = dto.Title;
        lesson.Content = dto.Content;
        lesson.VideoProvider = dto.VideoProvider;
        lesson.VideoRef = NormalizeVideoRef(dto.VideoProvider, dto.VideoRef);
        lesson.WorkbookUrl = dto.WorkbookUrl;
        lesson.SortOrder = dto.SortOrder;
        await db.SaveChangesAsync();
    }

    public async Task DeleteLessonAsync(int lessonId)
    {
        var lesson = await db.AcademyLessons.FindAsync(lessonId);
        if (lesson is null) return;
        db.AcademyLessons.Remove(lesson);
        await db.SaveChangesAsync();
    }

    // ---- Enrollments ----

    public async Task<List<AcademyEnrollmentDto>> GetEnrollmentsAsync(int? courseId = null)
    {
        var query = db.AcademyEnrollments.Include(e => e.Course).AsQueryable();
        if (courseId is not null)
            query = query.Where(e => e.CourseId == courseId);

        var enrollments = await query.OrderByDescending(e => e.EnrolledAt).ToListAsync();

        var userIds = enrollments.Select(e => e.ApplicationUserId).Distinct().ToList();
        var users = await userManager.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return enrollments.Select(e =>
        {
            users.TryGetValue(e.ApplicationUserId, out var u);
            var name = u is null ? "" : $"{u.FirstName} {u.LastName}".Trim();
            return new AcademyEnrollmentDto
            {
                Id = e.Id,
                CourseId = e.CourseId,
                CourseTitle = e.Course.Title,
                ApplicationUserId = e.ApplicationUserId,
                StudentEmail = u?.Email ?? "",
                StudentName = string.IsNullOrEmpty(name) ? (u?.Email ?? e.ApplicationUserId) : name,
                EnrolledAt = e.EnrolledAt,
                ExpiresAt = e.ExpiresAt,
                IsRevoked = e.IsRevoked,
                IsActive = e.IsActive
            };
        }).ToList();
    }

    public async Task<int> EnrollAsync(int courseId, string applicationUserId, DateTime? expiresAt = null)
    {
        var resolvedExpiry = expiresAt ?? await ResolveDefaultExpiryAsync(courseId);

        var existing = await db.AcademyEnrollments
            .FirstOrDefaultAsync(e => e.CourseId == courseId && e.ApplicationUserId == applicationUserId);

        if (existing is not null)
        {
            // Re-activate / extend an existing enrollment rather than violating the unique index.
            existing.IsRevoked = false;
            existing.ExpiresAt = resolvedExpiry;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var enrollment = new AcademyEnrollment
        {
            CourseId = courseId,
            ApplicationUserId = applicationUserId,
            EnrolledAt = DateTime.UtcNow,
            ExpiresAt = resolvedExpiry
        };
        db.AcademyEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        return enrollment.Id;
    }

    public async Task RevokeEnrollmentAsync(int enrollmentId, bool revoked)
    {
        var e = await db.AcademyEnrollments.FindAsync(enrollmentId)
            ?? throw new InvalidOperationException("Zapis nie istnieje.");
        e.IsRevoked = revoked;
        await db.SaveChangesAsync();
    }

    public async Task DeleteEnrollmentAsync(int enrollmentId)
    {
        var e = await db.AcademyEnrollments.FindAsync(enrollmentId);
        if (e is null) return;
        db.AcademyEnrollments.Remove(e);
        await db.SaveChangesAsync();
    }

    // Derive expiry from the course's DefaultAccessDays (null = lifetime access).
    private async Task<DateTime?> ResolveDefaultExpiryAsync(int courseId)
    {
        var course = await db.AcademyCourses.FindAsync(courseId);
        if (course?.DefaultAccessDays is int days and > 0)
            return DateTime.UtcNow.AddDays(days);
        return null;
    }

    private static string? NormalizeVideoRef(Domain.Enums.VideoProvider provider, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();

        // Owners may paste a full URL — extract the id/guid defensively.
        if (provider == Domain.Enums.VideoProvider.GoogleDrive)
        {
            // https://drive.google.com/file/d/<ID>/view  → <ID>
            var marker = "/d/";
            var idx = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = raw[(idx + marker.Length)..];
                var end = rest.IndexOf('/');
                return end >= 0 ? rest[..end] : rest;
            }
        }
        return raw;
    }

    private static AcademyLessonDto MapLesson(AcademyLesson l) => new()
    {
        Id = l.Id,
        ModuleId = l.ModuleId,
        Title = l.Title,
        Content = l.Content,
        VideoProvider = l.VideoProvider,
        VideoRef = l.VideoRef,
        WorkbookUrl = l.WorkbookUrl,
        SortOrder = l.SortOrder
    };
}
