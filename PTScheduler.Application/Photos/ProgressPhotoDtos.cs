using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Photos;

public class ProgressPhotoDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public DateOnly TakenOn { get; set; }
    public PhotoPose Pose { get; set; }
    public string? Note { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
    public bool UploadedByClient { get; set; }
    public DateTime UploadedAt { get; set; }

    public string Url => $"/photos/{Id}";
    public string ThumbUrl => $"/photos/{Id}?thumb=1";

    public static string PoseLabel(PhotoPose p) => p switch
    {
        PhotoPose.Front => "Przód",
        PhotoPose.Side => "Bok",
        PhotoPose.Back => "Tył",
        _ => "Inne"
    };
}
