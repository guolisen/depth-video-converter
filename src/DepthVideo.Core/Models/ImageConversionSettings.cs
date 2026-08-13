namespace DepthVideo.Core.Models;

public sealed record ImageConversionSettings(
    string InputPath,
    string OutputPath,
    string ModelPath,
    HardwareDevice Device,
    QualityPreset Quality,
    DepthPolarity Polarity)
{
    public int InferenceWidth => Quality switch
    {
        QualityPreset.Fast => 392,
        QualityPreset.Balanced => 518,
        QualityPreset.Fine => 700,
        _ => 518,
    };
}
