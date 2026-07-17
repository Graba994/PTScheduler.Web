using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

// Owner-facing: manage the course catalog (courses, modules, lessons) and enrollments.
public interface IAcademyCatalogService
{
    // Courses
    Task<List<AcademyCourseDto>> GetCoursesAsync();
    Task<AcademyCourseDto?> GetCourseAsync(int courseId);
    Task<int> CreateCourseAsync(AcademyCourseDto dto);
    Task UpdateCourseAsync(AcademyCourseDto dto);
    Task DeleteCourseAsync(int courseId);

    // Modules
    Task<List<AcademyModuleDto>> GetModulesAsync(int courseId);
    Task<int> CreateModuleAsync(AcademyModuleDto dto);
    Task UpdateModuleAsync(AcademyModuleDto dto);
    Task DeleteModuleAsync(int moduleId);

    // Lessons
    Task<AcademyLessonDto?> GetLessonAsync(int lessonId);
    Task<int> CreateLessonAsync(AcademyLessonDto dto);
    Task UpdateLessonAsync(AcademyLessonDto dto);
    Task DeleteLessonAsync(int lessonId);

    // Enrollments
    Task<List<AcademyEnrollmentDto>> GetEnrollmentsAsync(int? courseId = null);
    Task<int> EnrollAsync(int courseId, string applicationUserId, DateTime? expiresAt = null);
    Task RevokeEnrollmentAsync(int enrollmentId, bool revoked);
    Task DeleteEnrollmentAsync(int enrollmentId);
}
