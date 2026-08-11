using DepthVideo.Core.Inference;
using DepthVideo.Core.Models;

namespace DepthVideo.Core.Processing;

public sealed class DepthFrameProcessor
{
    private double? _low;
    private double? _high;

    public byte[] Convert(DepthPrediction prediction, DepthPolarity polarity, double stabilizationStrength)
    {
        var values = prediction.Values;
        if (values.Length == 0) return [];

        var (frameLow, frameHigh) = PercentileBounds(values);
        var keep = Math.Clamp(stabilizationStrength, 0, 0.95);
        _low = _low is null ? frameLow : _low.Value * keep + frameLow * (1 - keep);
        _high = _high is null ? frameHigh : _high.Value * keep + frameHigh * (1 - keep);
        var range = Math.Max(_high.Value - _low.Value, 1e-6);
        var output = new byte[values.Length];

        for (var index = 0; index < values.Length; index++)
        {
            var normalized = Math.Clamp((values[index] - _low.Value) / range, 0, 1);
            if (polarity == DepthPolarity.NearBlack) normalized = 1 - normalized;
            output[index] = (byte)Math.Round(normalized * 255);
        }
        return output;
    }

    public static (double Low, double High) PercentileBounds(ReadOnlySpan<float> values)
    {
        var stride = Math.Max(1, values.Length / 8192);
        var sample = new float[(values.Length + stride - 1) / stride];
        var target = 0;
        for (var index = 0; index < values.Length; index += stride)
        {
            sample[target++] = values[index];
        }
        Array.Sort(sample, 0, target);
        var last = Math.Max(0, target - 1);
        return (sample[(int)(last * 0.01)], sample[(int)(last * 0.99)]);
    }
}
