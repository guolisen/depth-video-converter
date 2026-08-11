namespace DepthVideo.Core.Inference;

public interface IDepthEstimator : IDisposable
{
    DepthPrediction Estimate(ReadOnlySpan<byte> rgb24, int width, int height);
}
