namespace DepthVideo.Core.Inference;

public sealed record DepthPrediction(float[] Values, int Width, int Height);
