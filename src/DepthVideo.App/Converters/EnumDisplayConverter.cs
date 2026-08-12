using System.Globalization;
using System.Windows.Data;
using DepthVideo.App.Localization;
using DepthVideo.Core.Models;

namespace DepthVideo.App.Converters;

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        QualityPreset.Fast => LocalizationService.Text("Fast"),
        QualityPreset.Balanced => LocalizationService.Text("Balanced"),
        QualityPreset.Fine => LocalizationService.Text("Fine"),
        DepthPolarity.NearWhite => LocalizationService.Text("NearWhite"),
        DepthPolarity.NearBlack => LocalizationService.Text("NearBlack"),
        VideoEncoder.Auto => LocalizationService.Text("AutoEncoder"),
        VideoEncoder.NvidiaH264 => LocalizationService.Text("NvidiaEncoder"),
        VideoEncoder.SoftwareH264 => LocalizationService.Text("SoftwareEncoder"),
        HardwareDevice device when device.Backend == ComputeBackend.Cpu => LocalizationService.Text("CpuCompatibility"),
        HardwareDevice device when device.IsHighPerformance => $"{device.Name}  {LocalizationService.Text("Recommended")}",
        HardwareDevice device => device.Name,
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
