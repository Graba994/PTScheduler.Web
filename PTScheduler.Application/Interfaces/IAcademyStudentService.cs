using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

// Student-facing: a Client sees their active enrollments, lesson content and tracks progress.
// Every method takes the student's applicationUserId and enforces access itself.
public interface IAcademyStudentService
{
    Task<List<MyCourseDto>> GetMyCoursesAsync(string applicationUserId);
    Task<MyCourseDto?> GetMyCourseAsync(string applicationUserId, int courseId);

    // Returns null if the student has no active access to the lesson's course.
    Task<MyLessonDetailDto?> GetLessonForStudentAsync(string applicationUserId, int lessonId);

    // No-op (returns false) if the student has no active access to the lesson.
    Task<bool> SetLessonCompletedAsync(string applicationUserId, int lessonId, bool completed);
}
