namespace DepthVideo.Core.Models;

public sealed record ImageMetadata(
    string FilePath,
    int Width,
    int Height,
    long FileSize,
    string Codec);
