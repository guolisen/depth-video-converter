using System.Diagnostics;
using DepthVideo.Core.Inference;
using DepthVideo.Core.Models;
using DepthVideo.Core.Processing;

namespace DepthVideo.Core.Services;

public sealed class FfmpegImageConversionService
{
    private readonly string _ffmpegPath;

    public FfmpegImageConversionService(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    public async Task ConvertAsync(
        ImageMetadata metadata,
        ImageConversionSettings settings,
        IProgress<ConversionProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var inferenceWidth = Math.Max(14, (int)Math.Round(settings.InferenceWidth / 14d) * 14);
        var inferenceHeight = Math.Max(14, (int)Math.Round(inferenceWidth * metadata.Height / (double)metadata.Width / 14) * 14);
        var frame = new byte[checked(inferenceWidth * inferenceHeight * 3)];
        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath) ?? Environment.CurrentDirectory);

        progress?.Report(new ConversionProgress(ConversionStage.LoadingModel, 10, 0, 1, 0, null, "正在加载深度模型"));
        log?.Invoke($"图片推理设备：{settings.Device.Name}；输入尺寸：{inferenceWidth}×{inferenceHeight}");
        using var estimator = new OnnxDepthEstimator(settings.ModelPath, settings.Device);

        progress?.Report(new ConversionProgress(ConversionStage.Decoding, 25, 0, 1, 0, null, "正在读取图片"));
        await DecodeAsync(settings.InputPath, inferenceWidth, inferenceHeight, frame, log, cancellationToken);

        progress?.Report(new ConversionProgress(ConversionStage.Inferring, 55, 0, 1, 0, null, "正在生成深度图片"));
        var prediction = estimator.Estimate(frame, inferenceWidth, inferenceHeight);
        var gray = new DepthFrameProcessor().Convert(prediction, settings.Polarity, 0);

        progress?.Report(new ConversionProgress(ConversionStage.Finalizing, 85, 1, 1, 0, null, "正在保存深度图片"));
        await EncodeAsync(settings.OutputPath, prediction.Width, prediction.Height, metadata.Width, metadata.Height, gray, log, cancellationToken);
        progress?.Report(new ConversionProgress(ConversionStage.Completed, 100, 1, 1, 0, TimeSpan.Zero, "转换完成"));
    }

    private async Task DecodeAsync(string inputPath, int width, int height, byte[] frame, Action<string>? log, CancellationToken cancellationToken)
    {
        var info = CreateStartInfo();
        info.RedirectStandardOutput = true;
        AddArguments(info, "-hide_banner", "-loglevel", "warning", "-i", inputPath, "-frames:v", "1",
            "-vf", $"scale={width}:{height}:flags=lanczos", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 FFmpeg 图片解码器。");
        var errorTask = DrainErrorAsync(process, log, cancellationToken);
        var offset = 0;
        while (offset < frame.Length)
        {
            var read = await process.StandardOutput.BaseStream.ReadAsync(frame.AsMemory(offset), cancellationToken);
            if (read == 0) break;
            offset += read;
        }
        await process.WaitForExitAsync(cancellationToken);
        await errorTask;
        if (process.ExitCode != 0 || offset != frame.Length) throw new InvalidOperationException("无法读取图片像素数据。");
    }

    private async Task EncodeAsync(string outputPath, int sourceWidth, int sourceHeight, int outputWidth, int outputHeight,
        byte[] gray, Action<string>? log, CancellationToken cancellationToken)
    {
        var info = CreateStartInfo();
        info.RedirectStandardInput = true;
        AddArguments(info, "-hide_banner", "-loglevel", "warning", "-y", "-f", "rawvideo", "-pix_fmt", "gray",
            "-video_size", $"{sourceWidth}x{sourceHeight}", "-i", "pipe:0", "-frames:v", "1",
            "-vf", $"scale={outputWidth}:{outputHeight}:flags=lanczos", outputPath);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 FFmpeg 图片编码器。");
        var errorTask = DrainErrorAsync(process, log, cancellationToken);
        await process.StandardInput.BaseStream.WriteAsync(gray, cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        await errorTask;
        if (process.ExitCode != 0 || !File.Exists(outputPath)) throw new InvalidOperationException("无法保存深度图片。");
    }

    private ProcessStartInfo CreateStartInfo() => new()
    {
        FileName = _ffmpegPath,
        UseShellExecute = false,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };

    private static void AddArguments(ProcessStartInfo info, params string[] arguments)
    {
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
    }

    private static async Task DrainErrorAsync(Process process, Action<string>? log, CancellationToken cancellationToken)
    {
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line)) log?.Invoke(line);
        }
    }
}
