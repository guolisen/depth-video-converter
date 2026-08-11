using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using DepthVideo.Core.Models;
using DepthVideo.Core.Services;

namespace DepthVideo.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly HardwareDetector _hardwareDetector = new();
    private readonly string? _ffmpegPath = ExecutableLocator.Find("ffmpeg");
    private readonly string? _ffprobePath = ExecutableLocator.Find("ffprobe");
    private readonly string _gpuModelPath = Path.Combine(AppContext.BaseDirectory, "models", "depth_anything_v2_small_fp16.onnx");
    private readonly string _cpuModelPath = Path.Combine(AppContext.BaseDirectory, "models", "depth_anything_v2_small_q8.onnx");

    private VideoMetadata? _video;
    private HardwareDevice? _selectedDevice;
    private QualityPreset _selectedQuality = QualityPreset.Balanced;
    private DepthPolarity _selectedPolarity = DepthPolarity.NearWhite;
    private VideoEncoder _selectedEncoder = VideoEncoder.Auto;
    private string _outputPath = string.Empty;
    private string _statusTitle = "请选择一个视频";
    private string _statusDetail = "支持 MP4、MOV、MKV、AVI 和 WebM";
    private double _progressValue;
    private bool _isBusy;
    private string _speedText = string.Empty;
    private CancellationTokenSource? _cancellation;

    public ObservableCollection<HardwareDevice> Devices { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public IReadOnlyList<QualityPreset> QualityOptions { get; } = Enum.GetValues<QualityPreset>();
    public IReadOnlyList<DepthPolarity> PolarityOptions { get; } = Enum.GetValues<DepthPolarity>();
    public IReadOnlyList<VideoEncoder> EncoderOptions { get; } = Enum.GetValues<VideoEncoder>();

    public VideoMetadata? Video
    {
        get => _video;
        private set
        {
            if (SetProperty(ref _video, value))
            {
                OnPropertyChanged(nameof(HasVideo));
                OnPropertyChanged(nameof(InputName));
                OnPropertyChanged(nameof(InputDetails));
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public bool HasVideo => Video is not null;
    public string InputName => Video is null ? "拖入视频或点击选择" : Path.GetFileName(Video.FilePath);
    public string InputDetails => Video is null
        ? "视频只在本机处理，不会上传"
        : $"{Video.Width} × {Video.Height} · {Video.FramesPerSecond:0.###} FPS · {Video.Duration:hh\\:mm\\:ss} · {FormatBytes(Video.FileSize)}";

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

    public string DeviceStatus => SelectedDevice is null ? "正在检测硬件" : $"{SelectedDevice.Name} · {SelectedDevice.Detail}";
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

    public bool CanStart => HasVideo && SelectedDevice is not null && !string.IsNullOrWhiteSpace(OutputPath) && !IsBusy;

    public Task InitializeAsync()
    {
        Devices.Clear();
        foreach (var device in _hardwareDetector.Detect()) Devices.Add(device);
        SelectedDevice = Devices.FirstOrDefault(device => device.IsHighPerformance) ?? Devices.FirstOrDefault();
        AddLog($"FFmpeg：{_ffmpegPath ?? "未找到"}");
        AddLog($"GPU 模型：{(File.Exists(_gpuModelPath) ? "已就绪" : "缺失")}；CPU 模型：{(File.Exists(_cpuModelPath) ? "已就绪" : "缺失")}");
        return Task.CompletedTask;
    }

    public async Task LoadVideoAsync(string filePath)
    {
        if (IsBusy) return;
        if (_ffprobePath is null)
        {
            SetError("没有找到 FFprobe，请安装 FFmpeg 或将其放入程序 tools 目录。");
            return;
        }

        try
        {
            StatusTitle = "正在读取视频";
            StatusDetail = Path.GetFileName(filePath);
            Video = await new FfprobeService(_ffprobePath).ProbeAsync(filePath);
            OutputPath = Path.Combine(
                Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory,
                $"{Path.GetFileNameWithoutExtension(filePath)}_黑白深度.mp4");
            ProgressValue = 0;
            StatusTitle = "视频已就绪";
            StatusDetail = "确认设置后开始转换";
            AddLog($"已载入 {InputName}，{InputDetails}");
        }
        catch (Exception exception)
        {
            Video = null;
            SetError(exception.Message);
        }
    }

    public async Task StartAsync()
    {
        if (!CanStart || Video is null || SelectedDevice is null) return;
        if (_ffmpegPath is null) { SetError("没有找到 FFmpeg。"); return; }
        var modelPath = SelectedDevice.Backend == ComputeBackend.Cpu ? _cpuModelPath : _gpuModelPath;
        if (!File.Exists(modelPath)) { SetError("当前设备需要的深度模型文件缺失。"); return; }

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        Logs.Clear();
        AddLog("转换任务开始");
        try
        {
            var settings = new ConversionSettings(
                Video.FilePath, OutputPath, modelPath, SelectedDevice,
                SelectedQuality, SelectedPolarity, SelectedEncoder);
            var converter = new FfmpegConversionService(_ffmpegPath);
            await converter.ConvertAsync(Video, settings, new Progress<ConversionProgress>(UpdateProgress), AddLog, _cancellation.Token);
            StatusTitle = "转换完成";
            StatusDetail = Path.GetFileName(OutputPath);
            SpeedText = "文件已保存";
        }
        catch (OperationCanceledException)
        {
            StatusTitle = "转换已停止";
            StatusDetail = "没有修改原视频";
            SpeedText = string.Empty;
            AddLog("用户取消了转换");
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
        StatusTitle = progress.Message;
        StatusDetail = progress.Stage switch
        {
            ConversionStage.LoadingModel => $"正在初始化 {SelectedDevice?.Name}",
            ConversionStage.Finalizing => "正在合并音频并完成文件",
            ConversionStage.Completed => "处理完成",
            _ => $"{progress.ProcessedFrames:N0} / {progress.TotalFrames:N0} 帧",
        };
        SpeedText = progress.FramesPerSecond > 0
            ? $"{progress.FramesPerSecond:0.0} FPS · 剩余 {FormatRemaining(progress.Remaining)}"
            : string.Empty;
    }

    private void SetError(string message)
    {
        StatusTitle = "无法完成操作";
        StatusDetail = message;
        SpeedText = string.Empty;
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
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatRemaining(TimeSpan? remaining) => remaining is null
        ? "计算中"
        : remaining.Value.TotalHours >= 1 ? remaining.Value.ToString(@"hh\:mm\:ss") : remaining.Value.ToString(@"mm\:ss");
}
