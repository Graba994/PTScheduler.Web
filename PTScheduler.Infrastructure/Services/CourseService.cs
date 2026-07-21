using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class CourseService(IDbContextFactory<ApplicationDbContext> dbFactory, IWebRootPathProvider webRoot) : ICourseService
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
                DurationText = c.DurationText,
                Level = c.Level,
                Author = c.Author,
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
                DurationText = c.DurationText,
                Level = c.Level,
                Author = c.Author,
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

    public async Task<string?> SelfEnrollFreeAsync(string userId, int courseId)
    {
        await using var db = dbFactory.CreateDbContext();

        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return "Kurs nie istnieje.";
        if (!course.IsPublished) return "Kurs nie jest dostępny.";
        if (course.Price > 0) return "Kurs jest płatny.";

        if (await HasActiveAccessAsync(userId, courseId)) return null; // already has access — idempotent

        db.CourseEnrollments.Add(new CourseEnrollment
        {
            CourseId = courseId,
            ApplicationUserId = userId,
            AccessType = CourseAccessType.Lifetime,
            Source = EnrollmentSource.Manual,
            GrantedAt = DateTime.UtcNow,
            ExpiresAt = null,
            Notes = "Dostęp bezpłatny"
        });
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string> UploadCoverAsync(int courseId, Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var dir = Path.Combine(webRoot.WebRootPath, "branding", "courses");
        Directory.CreateDirectory(dir);
        // Remove any previous cover for this course (any extension).
        foreach (var old in Directory.GetFiles(dir, $"{courseId}.*"))
            File.Delete(old);
        var filePath = Path.Combine(dir, $"{courseId}{ext}");
        await using (var fs = File.Create(filePath))
            await stream.CopyToAsync(fs);

        var relative = $"/branding/courses/{courseId}{ext}";
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId);
        if (c is not null) { c.CoverImageUrl = relative; await db.SaveChangesAsync(); }
        return relative;
    }

    public async Task DeleteCoverAsync(int courseId)
    {
        var dir = Path.Combine(webRoot.WebRootPath, "branding", "courses");
        if (Directory.Exists(dir))
            foreach (var old in Directory.GetFiles(dir, $"{courseId}.*"))
                File.Delete(old);
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == courseId);
        if (c is not null) { c.CoverImageUrl = null; await db.SaveChangesAsync(); }
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
                        ContentHtml = l.ContentHtml,
                        HasQuiz = l.QuizQuestions.Any(),
                        QuizPassThreshold = l.QuizPassThreshold
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

    // ---- Quizzes ----

    public async Task<QuizDto> GetQuizForEditAsync(int lessonId)
    {
        await using var db = dbFactory.CreateDbContext();
        var questions = await db.QuizQuestions.AsNoTracking().Include(q => q.Options)
            .Where(q => q.LessonId == lessonId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .ToListAsync();
        var threshold = await db.Lessons.Where(l => l.Id == lessonId)
            .Select(l => (int?)l.QuizPassThreshold).FirstOrDefaultAsync() ?? 70;
        return new QuizDto
        {
            LessonId = lessonId,
            PassThreshold = threshold,
            Questions = questions.Select(q => new QuizQuestionDto
            {
                Id = q.Id, Text = q.Text, Type = q.Type, SortOrder = q.SortOrder,
                Options = q.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
                    .Select(o => new QuizOptionDto { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect, SortOrder = o.SortOrder })
                    .ToList()
            }).ToList()
        };
    }

    public async Task SaveQuizAsync(int lessonId, int passThreshold, List<QuizQuestionDto> questions)
    {
        await using var db = dbFactory.CreateDbContext();
        var lesson = await db.Lessons.Include(l => l.QuizQuestions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(l => l.Id == lessonId);
        if (lesson is null) return;

        db.QuizOptions.RemoveRange(lesson.QuizQuestions.SelectMany(q => q.Options));
        db.QuizQuestions.RemoveRange(lesson.QuizQuestions);

        lesson.QuizPassThreshold = Math.Clamp(passThreshold, 1, 100);

        var qi = 0;
        foreach (var q in questions)
        {
            if (string.IsNullOrWhiteSpace(q.Text)) continue;
            var qe = new QuizQuestion { LessonId = lessonId, Text = q.Text.Trim(), Type = q.Type, SortOrder = qi++ };
            var oi = 0;
            foreach (var o in q.Options)
            {
                if (string.IsNullOrWhiteSpace(o.Text)) continue;
                qe.Options.Add(new QuizOption { Text = o.Text.Trim(), IsCorrect = o.IsCorrect, SortOrder = oi++ });
            }
            db.QuizQuestions.Add(qe);
        }
        await db.SaveChangesAsync();
    }

    public async Task<QuizDto?> GetQuizForStudentAsync(string userId, int lessonId)
    {
        var courseId = await GetLessonCourseId(lessonId);
        if (courseId is null || !await HasActiveAccessAsync(userId, courseId.Value)) return null;

        await using var db = dbFactory.CreateDbContext();
        var questions = await db.QuizQuestions.AsNoTracking().Include(q => q.Options)
            .Where(q => q.LessonId == lessonId)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Id)
            .ToListAsync();
        if (questions.Count == 0) return null;

        var threshold = await db.Lessons.Where(l => l.Id == lessonId).Select(l => l.QuizPassThreshold).FirstOrDefaultAsync();
        return new QuizDto
        {
            LessonId = lessonId,
            PassThreshold = threshold,
            Questions = questions.Select(q => new QuizQuestionDto
            {
                Id = q.Id, Text = q.Text, Type = q.Type, SortOrder = q.SortOrder,
                Options = q.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
                    // IsCorrect intentionally NOT exposed to students.
                    .Select(o => new QuizOptionDto { Id = o.Id, Text = o.Text, IsCorrect = false, SortOrder = o.SortOrder })
                    .ToList()
            }).ToList()
        };
    }

    public async Task<QuizResultDto?> SubmitQuizAsync(string userId, int lessonId, List<QuizAnswerDto> answers)
    {
        var courseId = await GetLessonCourseId(lessonId);
        if (courseId is null || !await HasActiveAccessAsync(userId, courseId.Value)) return null;

        await using var db = dbFactory.CreateDbContext();
        var questions = await db.QuizQuestions.AsNoTracking().Include(q => q.Options)
            .Where(q => q.LessonId == lessonId).ToListAsync();
        if (questions.Count == 0) return null;

        var answerMap = answers.GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.SelectMany(a => a.SelectedOptionIds).ToHashSet());
        var correctQ = new HashSet<int>();
        foreach (var q in questions)
        {
            var validOptionIds = q.Options.Select(o => o.Id).ToHashSet();
            var correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
            var selected = answerMap.TryGetValue(q.Id, out var s)
                ? s.Where(validOptionIds.Contains).ToHashSet()
                : [];
            if (correct.Count > 0 && selected.SetEquals(correct)) correctQ.Add(q.Id);
        }

        var total = questions.Count;
        var correctCount = correctQ.Count;
        var score = total == 0 ? 0 : (int)Math.Round(100.0 * correctCount / total);
        var threshold = await db.Lessons.Where(l => l.Id == lessonId).Select(l => l.QuizPassThreshold).FirstOrDefaultAsync();
        var passed = score >= threshold;

        var attempt = await db.QuizAttempts.FirstOrDefaultAsync(a => a.ApplicationUserId == userId && a.LessonId == lessonId);
        if (attempt is null)
            db.QuizAttempts.Add(new QuizAttempt { ApplicationUserId = userId, LessonId = lessonId, ScorePercent = score, Passed = passed, AttemptedAt = DateTime.UtcNow });
        else
        {
            attempt.ScorePercent = score;
            attempt.Passed = passed;
            attempt.AttemptedAt = DateTime.UtcNow;
        }

        if (passed)
        {
            var prog = await db.LessonProgress.FirstOrDefaultAsync(p => p.ApplicationUserId == userId && p.LessonId == lessonId);
            if (prog is null)
                db.LessonProgress.Add(new LessonProgress { ApplicationUserId = userId, LessonId = lessonId, CompletedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();

        return new QuizResultDto
        {
            ScorePercent = score,
            Passed = passed,
            PassThreshold = threshold,
            CorrectCount = correctCount,
            TotalCount = total,
            CorrectQuestionIds = correctQ
        };
    }

    public async Task<QuizAttemptDto?> GetLastAttemptAsync(string userId, int lessonId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.QuizAttempts.AsNoTracking()
            .Where(a => a.ApplicationUserId == userId && a.LessonId == lessonId)
            .Select(a => new QuizAttemptDto { ScorePercent = a.ScorePercent, Passed = a.Passed, AttemptedAt = a.AttemptedAt })
            .FirstOrDefaultAsync();
    }

    private async Task<int?> GetLessonCourseId(int lessonId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Lessons.Where(l => l.Id == lessonId)
            .Select(l => (int?)l.Module.CourseId).FirstOrDefaultAsync();
    }

    // ---- Student-facing (access-gated) ----

    public async Task<List<StudentCourseDto>> GetMyCoursesAsync(string userId)
    {
        var now = DateTime.UtcNow;
        await using var db = dbFactory.CreateDbContext();

        var courseIds = await db.CourseEnrollments.AsNoTracking()
            .Where(e => e.ApplicationUserId == userId && !e.IsRevoked
                && (e.ExpiresAt == null || e.ExpiresAt > now)
                && (e.StartsAt == null || e.StartsAt <= now))
            .Select(e => e.CourseId).Distinct().ToListAsync();

        if (courseIds.Count == 0) return [];

        var courses = await db.Courses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new StudentCourseDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                CoverImageUrl = c.CoverImageUrl,
                DurationText = c.DurationText,
                Level = c.Level,
                Author = c.Author,
                CreatedAt = c.CreatedAt,
                TotalLessons = c.Modules.SelectMany(m => m.Lessons).Count()
            }).ToListAsync();

        var completed = await db.LessonProgress.AsNoTracking()
            .Where(p => p.ApplicationUserId == userId && courseIds.Contains(p.Lesson.Module.CourseId))
            .GroupBy(p => p.Lesson.Module.CourseId)
            .Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToListAsync();
        var map = completed.ToDictionary(x => x.CourseId, x => x.Count);
        foreach (var c in courses) c.CompletedLessons = map.GetValueOrDefault(c.Id);
        return courses;
    }

    public async Task<StudentCourseDetailDto?> GetStudentCourseAsync(string userId, int courseId)
    {
        if (!await HasActiveAccessAsync(userId, courseId)) return null;

        await using var db = dbFactory.CreateDbContext();
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return null;

        var modules = await GetContentAsync(courseId);

        var completedIds = (await db.LessonProgress.AsNoTracking()
            .Where(p => p.ApplicationUserId == userId && p.Lesson.Module.CourseId == courseId)
            .Select(p => p.LessonId).ToListAsync()).ToHashSet();

        foreach (var m in modules)
            foreach (var l in m.Lessons)
                l.IsCompleted = completedIds.Contains(l.Id);

        return new StudentCourseDetailDto
        {
            Id = course.Id,
            Title = course.Title,
            DescriptionHtml = course.DescriptionHtml,
            Modules = modules,
            TotalLessons = modules.Sum(m => m.Lessons.Count),
            CompletedLessons = modules.SelectMany(m => m.Lessons).Count(l => l.IsCompleted)
        };
    }

    public async Task SetLessonCompletedAsync(string userId, int lessonId, bool completed)
    {
        await using var db = dbFactory.CreateDbContext();
        var courseId = await db.Lessons.Where(l => l.Id == lessonId)
            .Select(l => (int?)l.Module.CourseId).FirstOrDefaultAsync();
        if (courseId is null) return;
        if (!await HasActiveAccessAsync(userId, courseId.Value)) return;

        var existing = await db.LessonProgress
            .FirstOrDefaultAsync(p => p.ApplicationUserId == userId && p.LessonId == lessonId);
        if (completed && existing is null)
            db.LessonProgress.Add(new LessonProgress { ApplicationUserId = userId, LessonId = lessonId, CompletedAt = DateTime.UtcNow });
        else if (!completed && existing is not null)
            db.LessonProgress.Remove(existing);
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
        c.DurationText = string.IsNullOrWhiteSpace(dto.DurationText) ? null : dto.DurationText.Trim();
        c.Level = string.IsNullOrWhiteSpace(dto.Level) ? null : dto.Level.Trim();
        c.Author = string.IsNullOrWhiteSpace(dto.Author) ? null : dto.Author.Trim();
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
