namespace DepthVideo.Core.Models;

public enum ConversionStage
{
    Preparing,
    LoadingModel,
    Decoding,
    Inferring,
    Encoding,
    Finalizing,
    Completed,
}

public sealed record ConversionProgress(
    ConversionStage Stage,
    double Percent,
    long ProcessedFrames,
    long TotalFrames,
    double FramesPerSecond,
    TimeSpan? Remaining,
    string Message);
