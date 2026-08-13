using System.Diagnostics;
using DepthVideo.Core.Models;
using DepthVideo.Core.Services;
using Xunit.Abstractions;

namespace DepthVideo.Core.Tests;

public sealed class FfmpegImageConversionServiceTests
{
    private readonly ITestOutputHelper _output;

    public FfmpegImageConversionServiceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ConvertsImageToDepthPngAtOriginalSize()
    {
        var ffmpeg = ExecutableLocator.Find("ffmpeg");
        var ffprobe = ExecutableLocator.Find("ffprobe");
        if (ffmpeg is null || ffprobe is null) return;

        var testDirectory = Path.Combine(Path.GetTempPath(), $"depth-image-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var input = Path.Combine(testDirectory, "input.png");
        var output = Path.Combine(testDirectory, "depth.png");
        try
        {
            await CreateSampleImageAsync(ffmpeg, input);
            var metadata = await new FfprobeService(ffprobe).ProbeImageAsync(input);
            Assert.Equal(640, metadata.Width);
            Assert.Equal(360, metadata.Height);

            var device = new HardwareDetector().Detect().FirstOrDefault(candidate => candidate.IsHighPerformance)
                         ?? new HardwareDevice("cpu", "CPU", ComputeBackend.Cpu, 0, false, "test");
            var settings = new ImageConversionSettings(input, output, FindModel(), device, QualityPreset.Fast,
                DepthPolarity.NearWhite);
            await new FfmpegImageConversionService(ffmpeg).ConvertAsync(metadata, settings, null, _output.WriteLine,
                CancellationToken.None);

            Assert.True(File.Exists(output));
            var result = await new FfprobeService(ffprobe).ProbeImageAsync(output);
            Assert.Equal(metadata.Width, result.Width);
            Assert.Equal(metadata.Height, result.Height);
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
    }

    private static async Task CreateSampleImageAsync(string ffmpeg, string output)
    {
        var info = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
                     "testsrc2=size=640x360:rate=1", "-frames:v", "1", output,
                 })
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start FFmpeg.");
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
