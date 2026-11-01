using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RaceDay.Web.Models.Events;
using SkiaSharp;

namespace RaceDay.Web.Services;

public sealed class EventMediaStorageService
{
    private const int ImageMediaType = 0;
    private const int VideoMediaType = 1;
    private readonly string storageRoot;
    private readonly string publicPath;
    private readonly EventMediaStorageOptions options;

    public EventMediaStorageService(
        IOptions<EventMediaStorageOptions> options,
        IWebHostEnvironment environment)
    {
        this.options = options.Value;
        publicPath = this.options.PublicPath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(publicPath)
            || !publicPath.StartsWith("/", StringComparison.Ordinal)
            || publicPath.StartsWith("//", StringComparison.Ordinal)
            || publicPath.Contains("..", StringComparison.Ordinal)
            || publicPath.Contains('\\')
            || publicPath.Length > 50)
        {
            throw new InvalidOperationException("EventMedia:PublicPath must be a safe web path such as /EventMedia.");
        }

        var configuredDirectory = Path.IsPathRooted(this.options.StorageDirectory)
            ? this.options.StorageDirectory
            : Path.Combine(environment.ContentRootPath, this.options.StorageDirectory);
        storageRoot = Path.GetFullPath(configuredDirectory);

        var webRoot = Path.GetFullPath(environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (!IsWithin(webRoot, storageRoot))
        {
            throw new InvalidOperationException(
                "EventMedia:StorageDirectory must resolve beneath the application's wwwroot directory.");
        }
    }

    public async Task<StoredEventMedia> SaveImageAsync(
        IFormFile file,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        ValidateUploadSize(file, options.MaximumImageBytes, "image");
        await using var validationStream = file.OpenReadStream();
        using var codec = SKCodec.Create(validationStream);
        if (codec is null)
        {
            throw new EventMediaValidationException(
                "The selected file is not a valid JPEG, PNG, or WebP image.");
        }

        var (contentType, extension) = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ("image/jpeg", ".jpg"),
            SKEncodedImageFormat.Png => ("image/png", ".png"),
            SKEncodedImageFormat.Webp => ("image/webp", ".webp"),
            _ => throw new EventMediaValidationException(
                "Images must contain valid JPEG, PNG, or WebP data.")
        };

        if (codec.Info.Width <= 0
            || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > 40_000_000)
        {
            throw new EventMediaValidationException(
                "The image dimensions are invalid or too large to process.");
        }

        using var decodedImage = SKBitmap.Decode(codec);
        if (decodedImage is null)
        {
            throw new EventMediaValidationException(
                "The selected image is malformed or cannot be decoded.");
        }

        var relativePath = await StoreUploadAsync(file, extension, cancellationToken);
        return new StoredEventMedia(
            ImageMediaType,
            relativePath,
            contentType,
            file.Length,
            null,
            displayOrder);
    }

    public async Task<StoredEventMedia> SaveVideoAsync(
        IFormFile file,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        ValidateUploadSize(file, options.MaximumVideoBytes, "video");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".mp4" or ".webm"))
        {
            throw new EventMediaValidationException("Videos must be MP4 or WebM files.");
        }

        var relativePath = await StoreUploadAsync(file, extension, cancellationToken);
        try
        {
            var duration = await ValidateVideoAsync(relativePath, extension, cancellationToken);
            return new StoredEventMedia(
                VideoMediaType,
                relativePath,
                extension == ".mp4" ? "video/mp4" : "video/webm",
                file.Length,
                duration,
                displayOrder);
        }
        catch
        {
            await DeleteAsync(relativePath, CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var filePath = ResolveStoredPath(relativePath);
        if (filePath is null)
        {
            throw new InvalidOperationException("The stored media path is outside the configured media directory.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath))
        {
            return false;
        }

        await Task.Run(() => File.Delete(filePath), cancellationToken);
        return true;
    }

    public bool IsSafeStoredPath(string relativePath) =>
        ResolveStoredPath(relativePath) is not null;

    private async Task<string> StoreUploadAsync(
        IFormFile file,
        string extension,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(storageRoot);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(storageRoot, fileName);
        try
        {
            await using var source = file.OpenReadStream();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }
        catch
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            throw;
        }

        return $"{publicPath}/{fileName}";
    }

    private async Task<double> ValidateVideoAsync(
        string relativePath,
        string extension,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.FfprobePath))
        {
            throw new EventMediaValidationException(
                "Video validation is not configured. Set EventMedia:FfprobePath to a FFprobe executable.");
        }

        var storedPath = ResolveStoredPath(relativePath)
            ?? throw new InvalidOperationException("The video path is outside the configured media directory.");
        var startInfo = new ProcessStartInfo
        {
            FileName = options.FfprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration,format_name:stream=codec_type,codec_name");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(storedPath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new EventMediaValidationException("FFprobe could not be started.");
            }
        }
        catch (EventMediaValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new EventMediaValidationException(
                "FFprobe is unavailable. Install FFmpeg and set EventMedia:FfprobePath to its ffprobe executable.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new EventMediaValidationException("FFprobe could not validate the video within the allowed time.");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new EventMediaValidationException(
                string.IsNullOrWhiteSpace(error)
                    ? "The video is malformed or unsupported."
                    : "The video is malformed or unsupported. Choose a valid MP4 or WebM video.");
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var format = root.GetProperty("format");
            var formatName = format.GetProperty("format_name").GetString() ?? string.Empty;
            var duration = double.Parse(
                format.GetProperty("duration").GetString() ?? string.Empty,
                CultureInfo.InvariantCulture);
            var videoStream = root.GetProperty("streams")
                .EnumerateArray()
                .FirstOrDefault(stream =>
                    string.Equals(stream.GetProperty("codec_type").GetString(), "video", StringComparison.Ordinal));
            var codec = videoStream.ValueKind == JsonValueKind.Undefined
                ? string.Empty
                : videoStream.GetProperty("codec_name").GetString() ?? string.Empty;
            var formatMatches = extension == ".mp4"
                ? formatName.Contains("mp4", StringComparison.OrdinalIgnoreCase) && codec == "h264"
                : formatName.Contains("webm", StringComparison.OrdinalIgnoreCase)
                    && codec is "vp8" or "vp9" or "av1";

            if (!formatMatches || !double.IsFinite(duration) || duration <= 0)
            {
                throw new EventMediaValidationException(
                    "The video content must be a browser-compatible MP4 (H.264) or WebM (VP8, VP9, or AV1).");
            }

            if (duration > options.MaximumVideoDurationSeconds)
            {
                throw new EventMediaValidationException(
                    $"Videos must be {options.MaximumVideoDurationSeconds:0} seconds or shorter.");
            }

            return duration;
        }
        catch (EventMediaValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new EventMediaValidationException("FFprobe could not read valid video metadata.");
        }
    }

    private string? ResolveStoredPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || !relativePath.StartsWith($"{publicPath}/", StringComparison.Ordinal)
            || relativePath.Contains("..", StringComparison.Ordinal)
            || relativePath.Contains('\\')
            || relativePath.Count(character => character == '/') != 2)
        {
            return null;
        }

        var fileName = relativePath[(publicPath.Length + 1)..];
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(storageRoot, fileName));
        return IsWithin(storageRoot, fullPath) ? fullPath : null;
    }

    private static void ValidateUploadSize(IFormFile file, long maximumBytes, string mediaName)
    {
        if (file.Length <= 0)
        {
            throw new EventMediaValidationException($"Select a non-empty {mediaName} file.");
        }

        if (file.Length > maximumBytes)
        {
            throw new EventMediaValidationException(
                $"Each {mediaName} file must be no larger than {maximumBytes / (1024 * 1024)} MB.");
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
