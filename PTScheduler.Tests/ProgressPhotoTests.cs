using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Infrastructure.Services.Photos;
using PTScheduler.Tests.Helpers;
using SkiaSharp;
using Xunit;

namespace PTScheduler.Tests;

public class ProgressPhotoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ptphotos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>JPEG: lewa połowa czerwona, prawa niebieska; opcjonalnie z orientacją EXIF.</summary>
    private static byte[] MakeJpeg(int w, int h, ushort? exifOrientation = null)
    {
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            c.DrawRect(0, 0, w / 2f, h, red);
        }
        using var img = SKImage.FromBitmap(bmp);
        var jpeg = img.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
        if (exifOrientation is null) return jpeg;

        // Segment APP1 z jednym wpisem IFD: Orientation (0x0112) = wartość; do tego udawany GPS w komentarzu.
        var tiff = new List<byte>();
        tiff.AddRange("II*\0"u8.ToArray());
        tiff.AddRange(BitConverter.GetBytes(8u));
        tiff.AddRange(BitConverter.GetBytes((ushort)1));
        tiff.AddRange(BitConverter.GetBytes((ushort)0x0112));
        tiff.AddRange(BitConverter.GetBytes((ushort)3));
        tiff.AddRange(BitConverter.GetBytes(1u));
        tiff.AddRange(BitConverter.GetBytes(exifOrientation.Value));
        tiff.AddRange(new byte[2]);
        tiff.AddRange(BitConverter.GetBytes(0u));
        var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        var len = payload.Length + 2;
        var app1 = new byte[] { 0xFF, 0xE1, (byte)(len >> 8), (byte)(len & 0xFF) }.Concat(payload);
        return jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).ToArray();
    }

    [Fact]
    public void Compress_Shrinks_To_Max_Edge_As_WebP()
    {
        var result = PhotoCompressor.Compress(MakeJpeg(3000, 2000), 1600, 80);

        result.Width.Should().Be(1600);
        result.Height.Should().Be(1067);
        Encoding.ASCII.GetString(result.Data, 0, 4).Should().Be("RIFF");
        Encoding.ASCII.GetString(result.Data, 8, 4).Should().Be("WEBP");
    }

    [Fact]
    public void Compress_Does_Not_Upscale_Small_Images()
    {
        var result = PhotoCompressor.Compress(MakeJpeg(300, 400), 1600, 80);
        (result.Width, result.Height).Should().Be((300, 400));
    }

    [Fact]
    public void Compress_Applies_Exif_Rotation_And_Drops_Metadata()
    {
        var input = MakeJpeg(400, 200, exifOrientation: 6); // telefon trzymany pionowo
        var result = PhotoCompressor.Compress(input, 1600, 90);

        (result.Width, result.Height).Should().Be((200, 400));
        using var bmp = SKBitmap.Decode(result.Data);
        bmp.GetPixel(100, 20).Red.Should().BeGreaterThan(200, "lewa (czerwona) część zdjęcia trafia na górę");
        bmp.GetPixel(100, 380).Blue.Should().BeGreaterThan(200);
        Encoding.ASCII.GetString(result.Data).Should().NotContain("Exif");
    }

    [Fact]
    public void Compress_Rejects_Non_Images()
    {
        var act = () => PhotoCompressor.Compress(Encoding.UTF8.GetBytes("<html>nie obraz</html>"), 1600, 80);
        act.Should().Throw<InvalidDataException>();
    }

    private async Task<(ProgressPhotoService Svc, IDbContextFactory<ApplicationDbContext> F)> MakeAsync()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Clients.AddRange(
                new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K", TrainerUserId = "t1" },
                new Client { Id = 2, ApplicationUserId = "c2", FirstName = "Ola", LastName = "K", TrainerUserId = "t2" });
            await db.SaveChangesAsync();
        }
        var web = new Mock<IWebRootPathProvider>();
        web.SetupGet(w => w.WebRootPath).Returns(_root);
        var svc = new ProgressPhotoService(f, web.Object, TestClock.AtWallClock(new DateTime(2026, 10, 1, 9, 0, 0)),
            NullLogger<ProgressPhotoService>.Instance);
        return (svc, f);
    }

    [Fact]
    public async Task Access_Only_For_Client_Their_Trainer_And_Admin()
    {
        var (svc, _) = await MakeAsync();
        (await svc.CanAccessAsync(1, "c1", false)).Should().BeTrue();
        (await svc.CanAccessAsync(1, "t1", false)).Should().BeTrue();
        (await svc.CanAccessAsync(1, "t2", false)).Should().BeFalse();
        (await svc.CanAccessAsync(1, "c2", false)).Should().BeFalse();
        (await svc.CanAccessAsync(1, "", false)).Should().BeFalse();
        (await svc.CanAccessAsync(1, "owner", true)).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_Stores_Private_Files_And_Delete_Removes_Them()
    {
        var (svc, _) = await MakeAsync();
        var (ok, error) = await svc.UploadAsync(1, "c1", new MemoryStream(MakeJpeg(2400, 3200)),
            new DateOnly(2026, 10, 1), PhotoPose.Front, "  rano  ");

        ok.Should().BeTrue(error);
        var photo = (await svc.GetAsync(1)).Single();
        photo.Note.Should().Be("rano");
        photo.UploadedByClient.Should().BeTrue();
        (photo.Width, photo.Height).Should().Be((1200, 1600));

        var full = await svc.GetFileAsync(photo.Id, thumbnail: false);
        var thumb = await svc.GetFileAsync(photo.Id, thumbnail: true);
        full!.Value.Path.Should().StartWith(Path.Combine(_root, "branding", "_private", "photos", "1"));
        full.Value.ClientId.Should().Be(1);
        new FileInfo(thumb!.Value.Path).Length.Should().BeLessThan(new FileInfo(full.Value.Path).Length);

        (await svc.DeleteAsync(photo.Id, clientId: 2)).Should().BeFalse("zdjęcie należy do innego klienta");
        (await svc.DeleteAsync(photo.Id, clientId: 1)).Should().BeTrue();
        File.Exists(full.Value.Path).Should().BeFalse();
        File.Exists(thumb.Value.Path).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_Rejects_Invalid_File_And_Future_Date()
    {
        var (svc, _) = await MakeAsync();
        (await svc.UploadAsync(1, "c1", new MemoryStream("abc"u8.ToArray()), new DateOnly(2026, 10, 1), PhotoPose.Side, null))
            .Ok.Should().BeFalse();
        (await svc.UploadAsync(1, "c1", new MemoryStream(MakeJpeg(100, 100)), new DateOnly(2026, 12, 1), PhotoPose.Side, null))
            .Ok.Should().BeFalse();
        (await svc.GetAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAll_Removes_Rows_And_Folder()
    {
        var (svc, f) = await MakeAsync();
        await svc.UploadAsync(1, "t1", new MemoryStream(MakeJpeg(200, 300)), new DateOnly(2026, 9, 1), PhotoPose.Back, null);
        await svc.UploadAsync(1, "t1", new MemoryStream(MakeJpeg(200, 300)), new DateOnly(2026, 9, 29), PhotoPose.Back, null);

        await svc.DeleteAllForClientAsync(1);

        await using var db = f.CreateDbContext();
        (await db.ProgressPhotos.CountAsync()).Should().Be(0);
        Directory.Exists(Path.Combine(_root, "branding", "_private", "photos", "1")).Should().BeFalse();
    }
}
