using DepthVideo.Core.Inference;
using DepthVideo.Core.Models;
using DepthVideo.Core.Processing;

namespace DepthVideo.Core.Tests;

public sealed class DepthFrameProcessorTests
{
    [Fact]
    public void PercentileBoundsIgnoreExtremeOutliers()
    {
        var values = Enumerable.Range(0, 1000).Select(index => (float)index).ToArray();
        values[0] = -100_000;
        values[^1] = 100_000;

        var (low, high) = DepthFrameProcessor.PercentileBounds(values);

        Assert.InRange(low, 8, 12);
        Assert.InRange(high, 988, 992);
    }

    [Fact]
    public void ConvertCreatesFullGrayscaleRange()
    {
        var processor = new DepthFrameProcessor();
        var prediction = new DepthPrediction([0, 0.25f, 0.5f, 0.75f, 1], 5, 1);

        var result = processor.Convert(prediction, DepthPolarity.NearWhite, 0.7);

        Assert.Equal(0, result[0]);
        Assert.Equal(255, result[^1]);
        Assert.True(result[2] > result[1]);
    }
}
