using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using DepthVideo.App.Localization;
using DepthVideo.Core.Models;
using DepthVideo.Core.Services;

namespace DepthVideo.App.ViewModels;

public sealed class LocalizedOption<T>(T value, Func<string> displayNameFactory) : ObservableObject
{
    public T Value { get; } = value;
    public string DisplayName => displayNameFactory();

    public void Refresh() => OnPropertyChanged(nameof(DisplayName));
}

public sealed class MainViewModel : ObservableObject
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff",
    };
    private readonly HardwareDetector _hardwareDetector = new();
    private readonly string? _ffmpegPath = ExecutableLocator.Find("ffmpeg");
    private readonly string? _ffprobePath = ExecutableLocator.Find("ffprobe");
    private readonly string _gpuModelPath = Path.Combine(AppContext.BaseDirectory, "models", "depth_anything_v2_small_fp16.onnx");
    private readonly string _cpuModelPath = Path.Combine(AppContext.BaseDirectory, "models", "depth_anything_v2_small_q8.onnx");

    private VideoMetadata? _video;
    private ImageMetadata? _image;
    private HardwareDevice? _selectedDevice;
    private QualityPreset _selectedQuality = QualityPreset.Balanced;
    private DepthPolarity _selectedPolarity = DepthPolarity.NearWhite;
    private VideoEncoder _selectedEncoder = VideoEncoder.Auto;
    private LanguageOption _selectedLanguage;
    private string _outputPath = string.Empty;
    private string _statusTitle;
    private string _statusDetail;
    private double _progressValue;
    private bool _isBusy;
    private string _speedText = string.Empty;
    private CancellationTokenSource? _cancellation;

    public MainViewModel()
    {
        _selectedLanguage = LanguageOptions.First(language => language.Code == LocalizationService.CurrentLanguage);
        _statusTitle = LocalizationService.Text("ChooseMediaStatus");
        _statusDetail = LocalizationService.Text("SupportedMediaFormatsSentence");

        QualityOptions =
        [
            new(QualityPreset.Fast, () => LocalizationService.Text("Fast")),
            new(QualityPreset.Balanced, () => LocalizationService.Text("Balanced")),
            new(QualityPreset.Fine, () => LocalizationService.Text("Fine")),
        ];
        PolarityOptions =
        [
            new(DepthPolarity.NearWhite, () => LocalizationService.Text("NearWhite")),
            new(DepthPolarity.NearBlack, () => LocalizationService.Text("NearBlack")),
        ];
        EncoderOptions =
        [
            new(VideoEncoder.Auto, () => LocalizationService.Text("AutoEncoder")),
            new(VideoEncoder.NvidiaH264, () => LocalizationService.Text("NvidiaEncoder")),
            new(VideoEncoder.IntelH264, () => LocalizationService.Text("IntelEncoder")),
            new(VideoEncoder.AmdH264, () => LocalizationService.Text("AmdEncoder")),
            new(VideoEncoder.SoftwareH264, () => LocalizationService.Text("SoftwareEncoder")),
        ];
    }

    public ObservableCollection<HardwareDevice> Devices { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<LocalizedOption<HardwareDevice>> DeviceOptions { get; } = [];
    public IReadOnlyList<LocalizedOption<QualityPreset>> QualityOptions { get; }
    public IReadOnlyList<LocalizedOption<DepthPolarity>> PolarityOptions { get; }
    public IReadOnlyList<LocalizedOption<VideoEncoder>> EncoderOptions { get; }
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = LocalizationService.Languages;

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !SetProperty(ref _selectedLanguage, value)) return;
            LocalizationService.SetLanguage(value.Code);
            RefreshLocalizedContent();
        }
    }

    public VideoMetadata? Video
    {
        get => _video;
        private set
        {
            if (SetProperty(ref _video, value))
            {
                OnPropertyChanged(nameof(HasVideo));
                OnPropertyChanged(nameof(HasInput));
                OnPropertyChanged(nameof(IsVideo));
                OnPropertyChanged(nameof(IsImage));
                OnPropertyChanged(nameof(InputName));
                OnPropertyChanged(nameof(InputDetails));
                OnPropertyChanged(nameof(InputTypeLabel));
                OnPropertyChanged(nameof(DropPrompt));
                OnPropertyChanged(nameof(SupportedFormatsText));
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public ImageMetadata? Image
    {
        get => _image;
        private set
        {
            if (SetProperty(ref _image, value))
            {
                OnPropertyChanged(nameof(HasInput));
                OnPropertyChanged(nameof(IsVideo));
                OnPropertyChanged(nameof(IsImage));
                OnPropertyChanged(nameof(InputName));
                OnPropertyChanged(nameof(InputDetails));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(InputTypeLabel));
                OnPropertyChanged(nameof(DropPrompt));
                OnPropertyChanged(nameof(SupportedFormatsText));
            }
        }
    }

    public bool HasVideo => Video is not null;
    public bool HasInput => Video is not null || Image is not null;
    public bool IsVideo => Video is not null;
    public bool IsImage => Image is not null;
    public string InputTypeLabel => LocalizationService.Text(IsImage ? "InputImage" : "InputMedia");
    public string DropPrompt => LocalizationService.Text("DropMedia");
    public string SupportedFormatsText => LocalizationService.Text("SupportedMediaFormats");
    public string InputName => !HasInput
        ? LocalizationService.Text("DropOrChoose")
        : Path.GetFileName(Video?.FilePath ?? Image!.FilePath);
    public string InputDetails => !HasInput
        ? LocalizationService.Text("LocalOnly")
        : Video is not null
            ? $"{Video.Width} × {Video.Height} · {Video.FramesPerSecond:0.###} FPS · {Video.Duration:hh\\:mm\\:ss} · {FormatBytes(Video.FileSize)}"
            : $"{Image!.Width} × {Image.Height} · {FormatBytes(Image.FileSize)} · {Image.Codec.ToUpperInvariant()}";

    public HardwareDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(DeviceStatus));
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public string DeviceStatus => SelectedDevice is null
        ? LocalizationService.Text("DetectingHardware")
        : SelectedDevice.Backend == ComputeBackend.Cpu
            ? $"{LocalizationService.Text("CpuCompatibility")} · {LocalizationService.Text("SlowSpeed")}" 
            : $"{SelectedDevice.Name} · DirectML{(SelectedDevice.IsHighPerformance ? $" · {LocalizationService.Text("HighPerformanceGpu")}" : string.Empty)}";

    public QualityPreset SelectedQuality { get => _selectedQuality; set => SetProperty(ref _selectedQuality, value); }
    public DepthPolarity SelectedPolarity { get => _selectedPolarity; set => SetProperty(ref _selectedPolarity, value); }
    public VideoEncoder SelectedEncoder { get => _selectedEncoder; set => SetProperty(ref _selectedEncoder, value); }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (SetProperty(ref _outputPath, value)) OnPropertyChanged(nameof(CanStart));
        }
    }

    public string StatusTitle { get => _statusTitle; private set => SetProperty(ref _statusTitle, value); }
    public string StatusDetail { get => _statusDetail; private set => SetProperty(ref _statusDetail, value); }
    public double ProgressValue { get => _progressValue; private set => SetProperty(ref _progressValue, value); }
    public string SpeedText { get => _speedText; private set => SetProperty(ref _speedText, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanStart));
        }
    }

    public bool CanStart => HasInput && SelectedDevice is not null && !string.IsNullOrWhiteSpace(OutputPath) && !IsBusy;

    public Task InitializeAsync()
    {
        Devices.Clear();
        DeviceOptions.Clear();
        foreach (var device in _hardwareDetector.Detect())
        {
            Devices.Add(device);
            DeviceOptions.Add(new LocalizedOption<HardwareDevice>(device, () => GetDeviceDisplayName(device)));
        }
        SelectedDevice = Devices.FirstOrDefault(device => device.IsHighPerformance) ?? Devices.FirstOrDefault();
        AddLog(LocalizationService.Format("FfmpegLog", _ffmpegPath ?? LocalizationService.Text("NotFound")));
        AddLog(LocalizationService.Format(
            "ModelsLog",
            File.Exists(_gpuModelPath) ? LocalizationService.Text("Ready") : LocalizationService.Text("Missing"),
            File.Exists(_cpuModelPath) ? LocalizationService.Text("Ready") : LocalizationService.Text("Missing")));
        return Task.CompletedTask;
    }

    public async Task LoadFileAsync(string filePath)
    {
        if (IsBusy) return;
        if (_ffprobePath is null)
        {
            SetError(LocalizationService.Text("FfprobeMissing"));
            return;
        }

        try
        {
            var isImage = ImageExtensions.Contains(Path.GetExtension(filePath));
            StatusTitle = LocalizationService.Text(isImage ? "ReadingImage" : "ReadingVideo");
            StatusDetail = Path.GetFileName(filePath);
            if (isImage)
            {
                Image = await new FfprobeService(_ffprobePath).ProbeImageAsync(filePath);
                Video = null;
            }
            else
            {
                Video = await new FfprobeService(_ffprobePath).ProbeAsync(filePath);
                Image = null;
            }
            OutputPath = Path.Combine(
                Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory,
                $"{Path.GetFileNameWithoutExtension(filePath)}_{LocalizationService.Text("OutputSuffix")}{(isImage ? ".png" : ".mp4")}");
            ProgressValue = 0;
            StatusTitle = LocalizationService.Text(isImage ? "ImageReady" : "VideoReady");
            StatusDetail = LocalizationService.Text("ConfirmSettings");
            AddLog(LocalizationService.Format("LoadedVideoLog", InputName, InputDetails));
        }
        catch (Exception exception)
        {
            Video = null;
            Image = null;
            SetError(exception.Message);
        }
    }

    public async Task StartAsync()
    {
        if (!CanStart || SelectedDevice is null) return;
        if (_ffmpegPath is null)
        {
            SetError(LocalizationService.Text("FfmpegMissing"));
            return;
        }

        var modelPath = SelectedDevice.Backend == ComputeBackend.Cpu ? _cpuModelPath : _gpuModelPath;
        if (!File.Exists(modelPath))
        {
            SetError(LocalizationService.Text("ModelMissing"));
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        Logs.Clear();
        AddLog(LocalizationService.Text("ConversionStartedLog"));
        try
        {
            if (Image is not null)
            {
                var settings = new ImageConversionSettings(Image.FilePath, OutputPath, modelPath, SelectedDevice,
                    SelectedQuality, SelectedPolarity);
                await new FfmpegImageConversionService(_ffmpegPath).ConvertAsync(Image, settings,
                    new Progress<ConversionProgress>(UpdateProgress), AddLog, _cancellation.Token);
            }
            else if (Video is not null)
            {
                var settings = new ConversionSettings(Video.FilePath, OutputPath, modelPath, SelectedDevice,
                    SelectedQuality, SelectedPolarity, SelectedEncoder);
                await new FfmpegConversionService(_ffmpegPath).ConvertAsync(Video, settings,
                    new Progress<ConversionProgress>(UpdateProgress), AddLog, _cancellation.Token);
            }
            StatusTitle = LocalizationService.Text("ConversionComplete");
            StatusDetail = Path.GetFileName(OutputPath);
            SpeedText = LocalizationService.Text("FileSaved");
        }
        catch (OperationCanceledException)
        {
            StatusTitle = LocalizationService.Text("ConversionStopped");
            StatusDetail = LocalizationService.Text("OriginalUnchanged");
            SpeedText = string.Empty;
            AddLog(LocalizationService.Text("CancelledLog"));
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            AddLog(exception.ToString());
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    public void OpenOutputFolder()
    {
        var folder = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
    }

    private void UpdateProgress(ConversionProgress progress)
    {
        ProgressValue = progress.Percent;
        StatusTitle = progress.Stage switch
        {
            ConversionStage.LoadingModel => LocalizationService.Text("LoadingModel"),
            ConversionStage.Decoding or ConversionStage.Inferring or ConversionStage.Encoding => IsImage
                ? LocalizationService.Text("GeneratingDepthImage")
                : LocalizationService.Format("GeneratingDepthFrames", progress.ProcessedFrames, progress.TotalFrames),
            ConversionStage.Finalizing => LocalizationService.Text(IsImage ? "SavingDepthImage" : "MergingAudio"),
            ConversionStage.Completed => LocalizationService.Text("ConversionComplete"),
            _ => LocalizationService.Text("ProcessingComplete"),
        };
        StatusDetail = progress.Stage switch
        {
            ConversionStage.LoadingModel => LocalizationService.Format("InitializingDevice", SelectedDevice?.Name ?? string.Empty),
            ConversionStage.Finalizing => LocalizationService.Text(IsImage ? "SavingDepthImage" : "MergingAudio"),
            ConversionStage.Completed => LocalizationService.Text("ProcessingComplete"),
            _ => IsImage ? LocalizationService.Text("ImageProcessing") : LocalizationService.Format("FramesProgress", progress.ProcessedFrames, progress.TotalFrames),
        };
        SpeedText = progress.FramesPerSecond > 0
            ? LocalizationService.Format("SpeedRemaining", progress.FramesPerSecond, FormatRemaining(progress.Remaining))
            : string.Empty;
    }

    private void RefreshLocalizedContent()
    {
        OnPropertyChanged(nameof(InputName));
        OnPropertyChanged(nameof(InputDetails));
        OnPropertyChanged(nameof(InputTypeLabel));
        OnPropertyChanged(nameof(DropPrompt));
        OnPropertyChanged(nameof(SupportedFormatsText));
        OnPropertyChanged(nameof(DeviceStatus));

        foreach (var option in DeviceOptions) option.Refresh();
        foreach (var option in QualityOptions) option.Refresh();
        foreach (var option in PolarityOptions) option.Refresh();
        foreach (var option in EncoderOptions) option.Refresh();

        if (!IsBusy)
        {
            StatusTitle = !HasInput
                ? LocalizationService.Text("ChooseMediaStatus")
                : LocalizationService.Text(IsImage ? "ImageReady" : "VideoReady");
            StatusDetail = !HasInput
                ? LocalizationService.Text("SupportedMediaFormatsSentence")
                : LocalizationService.Text("ConfirmSettings");
        }
    }

    private void SetError(string message)
    {
        StatusTitle = LocalizationService.Text("OperationFailed");
        StatusDetail = message;
        SpeedText = string.Empty;
    }

    private static string GetDeviceDisplayName(HardwareDevice device)
    {
        if (device.Backend == ComputeBackend.Cpu) return LocalizationService.Text("CpuCompatibility");
        return device.IsHighPerformance
            ? $"{device.Name}  {LocalizationService.Text("Recommended")}"
            : device.Name;
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (Logs.Count > 200) Logs.RemoveAt(0);
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatRemaining(TimeSpan? remaining) => remaining is null
        ? LocalizationService.Text("Calculating")
        : remaining.Value.TotalHours >= 1
            ? remaining.Value.ToString(@"hh\:mm\:ss")
            : remaining.Value.ToString(@"mm\:ss");
}
