extern alias RaceDayWeb;

using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;
using RaceDayWeb::RaceDay.Web.Services;

namespace RaceDay.Tests;

public sealed class EventMediaStorageTests
{
    [Fact]
    public async Task SaveImageUsesDecodedFormatAndCreatesSafeStoredFile()
    {
        using var environment = new TemporaryMediaEnvironment();
        using var stream = new MemoryStream(CreatePng());
        var file = CreateFile(stream, "malicious-name.exe");

        var stored = await environment.Service.SaveImageAsync(file, 0, CancellationToken.None);

        Assert.Equal("image/png", stored.ContentType);
        Assert.EndsWith(".png", stored.RelativePath, StringComparison.Ordinal);
        Assert.True(environment.Service.IsSafeStoredPath(stored.RelativePath));
        Assert.True(File.Exists(environment.GetStoredPath(stored.RelativePath)));
        Assert.Matches("^/EventMedia/[a-f0-9]{32}\\.png$", stored.RelativePath);
    }

    [Fact]
    public async Task SaveImageRejectsMalformedImageWithoutCreatingAFile()
    {
        using var environment = new TemporaryMediaEnvironment();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not an image"));
        var file = CreateFile(stream, "image.jpg");

        await Assert.ThrowsAsync<EventMediaValidationException>(
            () => environment.Service.SaveImageAsync(file, 0, CancellationToken.None));

        Assert.Empty(environment.GetStoredFiles());
    }

    [Fact]
    public async Task SaveImageRejectsOversizedUploadBeforeDecoding()
    {
        using var environment = new TemporaryMediaEnvironment();
        using var stream = new MemoryStream(new byte[5 * 1024 * 1024 + 1]);
        var file = CreateFile(stream, "large.jpg");

        await Assert.ThrowsAsync<EventMediaValidationException>(
            () => environment.Service.SaveImageAsync(file, 0, CancellationToken.None));

        Assert.Empty(environment.GetStoredFiles());
    }

    [Fact]
    public async Task DeleteRemovesAStoredFileAndRejectsPathsOutsideMediaDirectory()
    {
        using var environment = new TemporaryMediaEnvironment();
        using var stream = new MemoryStream(CreatePng());
        var stored = await environment.Service.SaveImageAsync(
            CreateFile(stream, "image.png"),
            0,
            CancellationToken.None);

        Assert.True(await environment.Service.DeleteAsync(stored.RelativePath, CancellationToken.None));
        Assert.False(File.Exists(environment.GetStoredPath(stored.RelativePath)));
        Assert.False(await environment.Service.DeleteAsync(stored.RelativePath, CancellationToken.None));
        Assert.False(environment.Service.IsSafeStoredPath("/EventMedia/../../appsettings.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => environment.Service.DeleteAsync("/EventMedia/../../appsettings.json", CancellationToken.None));
    }

    [Fact]
    public async Task MissingFfprobeReturnsValidationErrorAndRemovesStoredVideo()
    {
        using var environment = new TemporaryMediaEnvironment();
        using var stream = new MemoryStream([0, 1, 2, 3]);
        var file = CreateFile(stream, "preview.mp4");

        var exception = await Assert.ThrowsAsync<EventMediaValidationException>(
            () => environment.Service.SaveVideoAsync(file, 0, CancellationToken.None));

        Assert.Contains("FFprobe is unavailable", exception.Message, StringComparison.Ordinal);
        Assert.Empty(environment.GetStoredFiles());
    }

    [Fact]
    public async Task StorageFailureIsSurfacedInsteadOfReturningAStoredReference()
    {
        using var environment = new TemporaryMediaEnvironment();
        var mediaDirectory = Path.Combine(environment.WebRootPath, "EventMedia");
        File.WriteAllText(mediaDirectory, "blocks directory creation");
        using var stream = new MemoryStream(CreatePng());

        await Assert.ThrowsAnyAsync<IOException>(
            () => environment.Service.SaveImageAsync(
                CreateFile(stream, "image.png"),
                0,
                CancellationToken.None));
    }

    private static IFormFile CreateFile(Stream stream, string fileName) =>
        new FormFile(stream, 0, stream.Length, "upload", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.Crimson);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class TemporaryMediaEnvironment : IWebHostEnvironment, IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            "RaceDayMediaTests",
            Guid.NewGuid().ToString("N"));

        public TemporaryMediaEnvironment()
        {
            ContentRootPath = root;
            WebRootPath = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
            var options = Options.Create(new EventMediaStorageOptions
            {
                StorageDirectory = "wwwroot/EventMedia",
                PublicPath = "/EventMedia",
                FfprobePath = Path.Combine(root, "missing-ffprobe.exe")
            });
            Service = new EventMediaStorageService(options, this);
        }

        public EventMediaStorageService Service { get; }

        public string ContentRootPath { get; set; }

        public string WebRootPath { get; set; }

        public string ApplicationName { get; set; } = "RaceDay.Tests";

        public string EnvironmentName { get; set; } = "Testing";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string GetStoredPath(string relativePath) =>
            Path.Combine(WebRootPath, "EventMedia", Path.GetFileName(relativePath));

        public string[] GetStoredFiles()
        {
            var directory = Path.Combine(WebRootPath, "EventMedia");
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory)
                : [];
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (File.Exists(Path.Combine(WebRootPath, "EventMedia")))
            {
                File.Delete(Path.Combine(WebRootPath, "EventMedia"));
            }
        }
    }
}
