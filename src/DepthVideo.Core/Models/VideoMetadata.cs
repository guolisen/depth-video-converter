namespace DepthVideo.Core.Models;

public sealed record VideoMetadata(
    string FilePath,
    int Width,
    int Height,
    double FramesPerSecond,
    TimeSpan Duration,
    long FileSize,
    string VideoCodec,
    bool HasAudio)
{
    public long EstimatedFrameCount => Math.Max(1, (long)Math.Round(Duration.TotalSeconds * FramesPerSecond));
}
