namespace DepthVideo.Core.Models;

public enum QualityPreset
{
    Fast,
    Balanced,
    Fine,
}

public enum DepthPolarity
{
    NearWhite,
    NearBlack,
}

public enum VideoEncoder
{
    Auto,
    NvidiaH264,
    SoftwareH264,
}

public sealed record ConversionSettings(
    string InputPath,
    string OutputPath,
    string ModelPath,
    HardwareDevice Device,
    QualityPreset Quality,
    DepthPolarity Polarity,
    VideoEncoder Encoder,
    double StabilizationStrength = 0.7)
{
    public int InferenceWidth => Quality switch
    {
        QualityPreset.Fast => 392,
        QualityPreset.Balanced => 518,
        QualityPreset.Fine => 700,
        _ => 518,
    };
}
