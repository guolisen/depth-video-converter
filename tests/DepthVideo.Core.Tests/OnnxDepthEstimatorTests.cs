using DepthVideo.Core.Inference;
using DepthVideo.Core.Models;
using DepthVideo.Core.Services;

namespace DepthVideo.Core.Tests;

public sealed class OnnxDepthEstimatorTests
{
    [Fact]
    public void CpuEstimatorProducesDepthFrame()
    {
        var modelPath = FindModel("depth_anything_v2_small_q8.onnx");
        var device = new HardwareDevice("cpu", "CPU", ComputeBackend.Cpu, 0, false, "test");
        using var estimator = new OnnxDepthEstimator(modelPath, device);
        var image = CreateGradient(392, 392);

        var result = estimator.Estimate(image, 392, 392);

        Assert.Equal(result.Width * result.Height, result.Values.Length);
        Assert.Contains(result.Values, value => float.IsFinite(value));
    }

    [Fact]
    public void PreferredGpuEstimatorProducesDepthFrame()
    {
        var device = new HardwareDetector().Detect().FirstOrDefault(candidate => candidate.IsHighPerformance);
        if (device is null) return;
        using var estimator = new OnnxDepthEstimator(FindModel("depth_anything_v2_small_fp16.onnx"), device);
        var image = CreateGradient(392, 392);

        var result = estimator.Estimate(image, 392, 392);

        Assert.Equal(result.Width * result.Height, result.Values.Length);
    }

    [Fact]
    public void PreferredGpuSupportsVideoAspectRatio()
    {
        var device = new HardwareDetector().Detect().FirstOrDefault(candidate => candidate.IsHighPerformance);
        if (device is null) return;
        using var estimator = new OnnxDepthEstimator(FindModel("depth_anything_v2_small_fp16.onnx"), device);
        var image = CreateGradient(392, 224);

        var result = estimator.Estimate(image, 392, 224);

        Assert.Equal(result.Width * result.Height, result.Values.Length);
    }

    private static string FindModel(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "models", fileName);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("Test model not found.");
    }

    private static byte[] CreateGradient(int width, int height)
    {
        var image = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 3;
                image[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
                image[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                image[offset + 2] = 128;
            }
        }
        return image;
    }
}
