using System.Globalization;
using System.Windows.Data;
using DepthVideo.Core.Models;

namespace DepthVideo.App.Converters;

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        QualityPreset.Fast => "快速",
        QualityPreset.Balanced => "标准",
        QualityPreset.Fine => "高质量",
        DepthPolarity.NearWhite => "近白远黑",
        DepthPolarity.NearBlack => "近黑远白",
        VideoEncoder.Auto => "自动（优先 NVIDIA）",
        VideoEncoder.NvidiaH264 => "NVIDIA H.264",
        VideoEncoder.SoftwareH264 => "软件 H.264",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
