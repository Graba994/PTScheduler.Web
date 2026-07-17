using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class AcademyLesson
{
    public int Id { get; set; }

    public int ModuleId { get; set; }
    public AcademyModule Module { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    // Rich text / HTML body of the lesson.
    public string? Content { get; set; }

    public VideoProvider VideoProvider { get; set; } = VideoProvider.None;

    // Provider-specific reference: Google Drive file id, Bunny video GUID, Vimeo id.
    // NOT a full URL — only the id/guid, extracted by the Owner when creating the lesson.
    public string? VideoRef { get; set; }

    // Optional external attachment (workbook PDF etc.). Plain URL for MVP — no upload.
    public string? WorkbookUrl { get; set; }

    public int SortOrder { get; set; } = 0;

    public ICollection<AcademyLessonProgress> Progress { get; set; } = [];
}
