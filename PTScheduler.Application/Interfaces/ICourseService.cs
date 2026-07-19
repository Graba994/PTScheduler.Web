using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ICourseService
{
    Task<List<CourseDto>> GetCoursesAsync(bool includeUnpublished = true);
    Task<CourseDto?> GetCourseAsync(int id);
    Task<int> CreateCourseAsync(SaveCourseDto dto);
    Task UpdateCourseAsync(int id, SaveCourseDto dto);
    Task DeleteCourseAsync(int id);

    Task<List<CourseEnrollmentDto>> GetEnrollmentsAsync(int courseId);
    Task<List<CourseEnrollmentDto>> GetUserEnrollmentsAsync(string userId);

    /// <summary>Grant (or re-grant) course access. Returns an error message on failure, null on success.</summary>
    Task<string> UploadCoverAsync(int courseId, Stream stream, string fileName);
    Task DeleteCoverAsync(int courseId);

    Task<string?> GrantAccessAsync(GrantEnrollmentDto dto, string grantedByUserId);
    Task RevokeAccessAsync(int enrollmentId);
    Task RestoreAccessAsync(int enrollmentId);

    Task<bool> HasActiveAccessAsync(string userId, int courseId);

    // ---- Course content: modules + lessons ----
    Task<List<ModuleDto>> GetContentAsync(int courseId);
    Task<int> AddModuleAsync(int courseId, string title);
    Task UpdateModuleAsync(int moduleId, string title);
    Task DeleteModuleAsync(int moduleId);
    Task MoveModuleAsync(int moduleId, int direction);
    Task<int> AddLessonAsync(int moduleId, SaveLessonDto dto);
    Task UpdateLessonAsync(int lessonId, SaveLessonDto dto);
    Task DeleteLessonAsync(int lessonId);
    Task MoveLessonAsync(int lessonId, int direction);

    // ---- Quizzes ----
    Task<QuizDto> GetQuizForEditAsync(int lessonId);
    Task SaveQuizAsync(int lessonId, int passThreshold, List<QuizQuestionDto> questions);
    Task<QuizDto?> GetQuizForStudentAsync(string userId, int lessonId);
    Task<QuizResultDto?> SubmitQuizAsync(string userId, int lessonId, List<QuizAnswerDto> answers);
    Task<QuizAttemptDto?> GetLastAttemptAsync(string userId, int lessonId);

    // ---- Student-facing (access-gated) ----
    Task<List<StudentCourseDto>> GetMyCoursesAsync(string userId);
    Task<StudentCourseDetailDto?> GetStudentCourseAsync(string userId, int courseId);
    Task SetLessonCompletedAsync(string userId, int lessonId, bool completed);
}
