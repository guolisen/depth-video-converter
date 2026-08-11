using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DepthVideo.Core.Models;

namespace DepthVideo.Core.Services;

public sealed class FfprobeService
{
    private readonly string _ffprobePath;

    public FfprobeService(string ffprobePath) => _ffprobePath = ffprobePath;

    public async Task<VideoMetadata> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", filePath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 FFprobe。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "无法读取视频信息。" : error.Trim());
        }

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("文件中没有可读取的视频轨道。");
        }

        var fpsText = video.TryGetProperty("avg_frame_rate", out var averageRate)
            ? averageRate.GetString()
            : video.GetProperty("r_frame_rate").GetString();
        var fps = ParseRate(fpsText);
        var format = root.GetProperty("format");
        var duration = ParseDouble(format.TryGetProperty("duration", out var durationValue) ? durationValue.GetString() : null);
        var fileSize = long.TryParse(format.TryGetProperty("size", out var sizeValue) ? sizeValue.GetString() : null, out var parsedSize)
            ? parsedSize
            : new FileInfo(filePath).Length;

        return new VideoMetadata(
            filePath,
            video.GetProperty("width").GetInt32(),
            video.GetProperty("height").GetInt32(),
            fps > 0 ? fps : 24,
            TimeSpan.FromSeconds(Math.Max(duration, 0.001)),
            fileSize,
            video.TryGetProperty("codec_name", out var codec) ? codec.GetString() ?? "unknown" : "unknown",
            streams.Any(stream => stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio"));
    }

    private static double ParseRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            Math.Abs(denominator) > double.Epsilon)
        {
            return numerator / denominator;
        }
        return ParseDouble(value);
    }

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
