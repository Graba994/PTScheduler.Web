using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class AcademyStudentService(ApplicationDbContext db) : IAcademyStudentService
{
    public async Task<List<MyCourseDto>> GetMyCoursesAsync(string applicationUserId)
    {
        var now = DateTime.UtcNow;

        var enrollments = await db.AcademyEnrollments
            .Include(e => e.Course).ThenInclude(c => c.Modules).ThenInclude(m => m.Lessons)
            .Where(e => e.ApplicationUserId == applicationUserId
                        && !e.IsRevoked
                        && (e.ExpiresAt == null || e.ExpiresAt > now))
            .ToListAsync();

        var lessonIds = enrollments
            .SelectMany(e => e.Course.Modules).SelectMany(m => m.Lessons)
            .Select(l => l.Id).ToList();

        var completed = await db.AcademyLessonProgress
            .Where(p => p.ApplicationUserId == applicationUserId
                        && p.IsCompleted
                        && lessonIds.Contains(p.LessonId))
            .Select(p => p.LessonId)
            .ToListAsync();
        var completedSet = completed.ToHashSet();

        return enrollments
            .OrderBy(e => e.Course.SortOrder).ThenBy(e => e.Course.Id)
            .Select(e => BuildMyCourse(e, completedSet))
            .ToList();
    }

    public async Task<MyCourseDto?> GetMyCourseAsync(string applicationUserId, int courseId)
    {
        var now = DateTime.UtcNow;

        var enrollment = await db.AcademyEnrollments
            .Include(e => e.Course).ThenInclude(c => c.Modules).ThenInclude(m => m.Lessons)
            .FirstOrDefaultAsync(e => e.ApplicationUserId == applicationUserId
                                      && e.CourseId == courseId
                                      && !e.IsRevoked
                                      && (e.ExpiresAt == null || e.ExpiresAt > now));

        if (enrollment is null) return null;

        var lessonIds = enrollment.Course.Modules.SelectMany(m => m.Lessons).Select(l => l.Id).ToList();
        var completed = await db.AcademyLessonProgress
            .Where(p => p.ApplicationUserId == applicationUserId
                        && p.IsCompleted
                        && lessonIds.Contains(p.LessonId))
            .Select(p => p.LessonId)
            .ToListAsync();

        return BuildMyCourse(enrollment, completed.ToHashSet());
    }

    public async Task<MyLessonDetailDto?> GetLessonForStudentAsync(string applicationUserId, int lessonId)
    {
        var lesson = await db.AcademyLessons
            .Include(l => l.Module).ThenInclude(m => m.Course).ThenInclude(c => c.Modules).ThenInclude(m => m.Lessons)
            .FirstOrDefaultAsync(l => l.Id == lessonId);

        if (lesson is null) return null;

        var course = lesson.Module.Course;
        if (!await HasActiveAccessAsync(applicationUserId, course.Id))
            return null;

        // Flatten the course's lessons in display order for prev/next navigation.
        var ordered = course.Modules
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .SelectMany(m => m.Lessons.OrderBy(l => l.SortOrder).ThenBy(l => l.Id))
            .ToList();

        var index = ordered.FindIndex(l => l.Id == lessonId);
        var prevId = index > 0 ? ordered[index - 1].Id : (int?)null;
        var nextId = index >= 0 && index < ordered.Count - 1 ? ordered[index + 1].Id : (int?)null;

        var isCompleted = await db.AcademyLessonProgress
            .AnyAsync(p => p.ApplicationUserId == applicationUserId
                           && p.LessonId == lessonId
                           && p.IsCompleted);

        return new MyLessonDetailDto
        {
            LessonId = lesson.Id,
            CourseId = course.Id,
            CourseTitle = course.Title,
            Title = lesson.Title,
            Content = lesson.Content,
            VideoProvider = lesson.VideoProvider,
            VideoRef = lesson.VideoRef,
            WorkbookUrl = lesson.WorkbookUrl,
            IsCompleted = isCompleted,
            PreviousLessonId = prevId,
            NextLessonId = nextId
        };
    }

    public async Task<bool> SetLessonCompletedAsync(string applicationUserId, int lessonId, bool completed)
    {
        var lesson = await db.AcademyLessons
            .Include(l => l.Module)
            .FirstOrDefaultAsync(l => l.Id == lessonId);

        if (lesson is null) return false;
        if (!await HasActiveAccessAsync(applicationUserId, lesson.Module.CourseId))
            return false;

        var progress = await db.AcademyLessonProgress
            .FirstOrDefaultAsync(p => p.ApplicationUserId == applicationUserId && p.LessonId == lessonId);

        if (progress is null)
        {
            progress = new AcademyLessonProgress
            {
                LessonId = lessonId,
                ApplicationUserId = applicationUserId
            };
            db.AcademyLessonProgress.Add(progress);
        }

        progress.IsCompleted = completed;
        progress.CompletedAt = completed ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        return true;
    }

    private async Task<bool> HasActiveAccessAsync(string applicationUserId, int courseId)
    {
        var now = DateTime.UtcNow;
        return await db.AcademyEnrollments.AnyAsync(e =>
            e.ApplicationUserId == applicationUserId
            && e.CourseId == courseId
            && !e.IsRevoked
            && (e.ExpiresAt == null || e.ExpiresAt > now));
    }

    private static MyCourseDto BuildMyCourse(AcademyEnrollment e, HashSet<int> completedLessonIds)
    {
        var modules = e.Course.Modules
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new MyModuleDto
            {
                ModuleId = m.Id,
                Title = m.Title,
                Lessons = m.Lessons
                    .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                    .Select(l => new MyLessonDto
                    {
                        LessonId = l.Id,
                        Title = l.Title,
                        IsCompleted = completedLessonIds.Contains(l.Id)
                    }).ToList()
            }).ToList();

        var total = modules.Sum(m => m.Lessons.Count);
        var done = modules.Sum(m => m.Lessons.Count(l => l.IsCompleted));

        return new MyCourseDto
        {
            CourseId = e.Course.Id,
            Title = e.Course.Title,
            Description = e.Course.Description,
            CoverImageUrl = e.Course.CoverImageUrl,
            ExpiresAt = e.ExpiresAt,
            TotalLessons = total,
            CompletedLessons = done,
            Modules = modules
        };
    }
}
