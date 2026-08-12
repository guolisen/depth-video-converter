using System.Diagnostics;
using DepthVideo.Core.Models;
using DepthVideo.Core.Services;
using Xunit.Abstractions;

namespace DepthVideo.Core.Tests;

public sealed class FfmpegConversionServiceTests
{
    private readonly ITestOutputHelper _output;

    public FfmpegConversionServiceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ConvertsShortVideoEndToEnd()
    {
        var ffmpeg = ExecutableLocator.Find("ffmpeg");
        var ffprobe = ExecutableLocator.Find("ffprobe");
        if (ffmpeg is null || ffprobe is null) return;

        var testDirectory = Path.Combine(Path.GetTempPath(), $"depth-video-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var input = Path.Combine(testDirectory, "input.mp4");
        var output = Path.Combine(testDirectory, "output.mp4");
        try
        {
            await CreateSampleAsync(ffmpeg, input);
            var metadata = await new FfprobeService(ffprobe).ProbeAsync(input);
            Assert.Equal(2, metadata.EstimatedFrameCount);
            var device = new HardwareDetector().Detect().FirstOrDefault(candidate => candidate.IsHighPerformance)
                         ?? new HardwareDevice("cpu", "CPU", ComputeBackend.Cpu, 0, false, "test");
            var settings = new ConversionSettings(
                input,
                output,
                FindModel(),
                device,
                QualityPreset.Fast,
                DepthPolarity.NearWhite,
                VideoEncoder.Auto);

            await new FfmpegConversionService(ffmpeg).ConvertAsync(
                metadata,
                settings,
                progress: null,
                log: _output.WriteLine,
                CancellationToken.None);

            Assert.True(File.Exists(output));
            Assert.True(new FileInfo(output).Length > 1_000);
            var result = await new FfprobeService(ffprobe).ProbeAsync(output);
            Assert.Equal(metadata.Width, result.Width);
            Assert.Equal(metadata.Height, result.Height);
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task FfmpegPipeReturnsRawFrame()
    {
        var ffmpeg = ExecutableLocator.Find("ffmpeg");
        if (ffmpeg is null) return;
        var testDirectory = Path.Combine(Path.GetTempPath(), $"depth-pipe-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var input = Path.Combine(testDirectory, "input.mp4");
        try
        {
            await CreateSampleAsync(ffmpeg, input);
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-nostdin", "-loglevel", "warning", "-i", input,
                         "-map", "0:v:0", "-an", "-vf", "scale=392:224:flags=lanczos",
                         "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1",
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start FFmpeg.");
            var buffer = new byte[392 * 224 * 3];
            var readTask = process.StandardOutput.BaseStream.ReadAsync(buffer).AsTask();
            var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed != readTask)
            {
                process.Kill(true);
                var error = await process.StandardError.ReadToEndAsync();
                throw new TimeoutException($"FFmpeg pipe timed out. {error}");
            }
            Assert.True(await readTask > 0);
            if (!process.HasExited) process.Kill(true);
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task ProbeUsesPlaybackFrameRateForSparseTimestampVideo()
    {
        var ffmpeg = ExecutableLocator.Find("ffmpeg");
        var ffprobe = ExecutableLocator.Find("ffprobe");
        if (ffmpeg is null || ffprobe is null) return;

        var testDirectory = Path.Combine(Path.GetTempPath(), $"depth-rate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var input = Path.Combine(testDirectory, "sparse.mp4");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
                     {
                         "-hide_banner", "-loglevel", "error", "-y",
                         "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=60:duration=2",
                         "-vf", "select='lt(mod(n,5),2)'", "-fps_mode", "vfr", "-c:v", "libx264", input,
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start FFmpeg.");
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var error = await errorTask;
            if (process.ExitCode != 0) throw new InvalidOperationException(error);

            var metadata = await new FfprobeService(ffprobe).ProbeAsync(input);
            Assert.Equal(60, metadata.FramesPerSecond, 3);
            Assert.InRange(metadata.EstimatedFrameCount, 117, 120);
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
    }

    private static async Task CreateSampleAsync(string ffmpeg, string output)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y",
                     "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=2:duration=1",
                     "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                     "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", output,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }

    private static string FindModel()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "models", "depth_anything_v2_small_fp16.onnx");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("Test model not found.");
    }
}
