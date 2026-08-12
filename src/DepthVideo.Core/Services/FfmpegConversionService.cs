using System.Diagnostics;
using System.Globalization;
using DepthVideo.Core.Inference;
using DepthVideo.Core.Models;
using DepthVideo.Core.Processing;

namespace DepthVideo.Core.Services;

public sealed class FfmpegConversionService
{
    private readonly string _ffmpegPath;

    public FfmpegConversionService(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    public async Task ConvertAsync(
        VideoMetadata metadata,
        ConversionSettings settings,
        IProgress<ConversionProgress>? progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var inferenceWidth = Math.Max(14, (int)Math.Round(settings.InferenceWidth / 14d) * 14);
        var inferenceHeight = Math.Max(14, (int)Math.Round(inferenceWidth * metadata.Height / (double)metadata.Width / 14) * 14);
        var frameSize = checked(inferenceWidth * inferenceHeight * 3);
        var totalFrames = metadata.EstimatedFrameCount;
        var partialPath = settings.OutputPath + ".partial.mp4";
        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath) ?? Environment.CurrentDirectory);
        TryDelete(partialPath);

        progress?.Report(new ConversionProgress(ConversionStage.LoadingModel, 2, 0, totalFrames, 0, null, "正在加载深度模型"));
        log?.Invoke($"推理设备：{settings.Device.Name}；输入尺寸：{inferenceWidth}×{inferenceHeight}");
        using var estimator = new OnnxDepthEstimator(settings.ModelPath, settings.Device);
        var frameProcessor = new DepthFrameProcessor();

        using var decoder = StartDecoder(settings.InputPath, inferenceWidth, inferenceHeight, metadata.FramesPerSecond);
        var decoderErrorTask = DrainErrorAsync(decoder, log, cancellationToken);
        Process? encoder = null;
        Task? encoderErrorTask = null;
        var frame = new byte[frameSize];
        var processed = 0L;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            log?.Invoke("FFmpeg 解码器已启动，等待第一帧");
            while (await ReadFrameAsync(decoder.StandardOutput.BaseStream, frame, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (processed == 0) log?.Invoke("第一帧已解码，开始深度推理");
                var prediction = estimator.Estimate(frame, inferenceWidth, inferenceHeight);
                if (processed == 0) log?.Invoke($"第一帧推理完成：{prediction.Width}×{prediction.Height}");
                var gray = frameProcessor.Convert(prediction, settings.Polarity, settings.StabilizationStrength);

                if (encoder is null)
                {
                    var encoderName = await ResolveEncoderAsync(settings.Encoder, settings.Device, log, cancellationToken);
                    encoder = StartEncoder(settings.InputPath, partialPath, prediction.Width, prediction.Height, metadata, encoderName);
                    encoderErrorTask = DrainErrorAsync(encoder, log, cancellationToken);
                    log?.Invoke($"视频编码器：{encoderName}；输出：{metadata.Width}×{metadata.Height}");
                }

                await encoder.StandardInput.BaseStream.WriteAsync(gray, cancellationToken);
                processed++;
                totalFrames = Math.Max(totalFrames, processed);
                var elapsed = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                var speed = processed / elapsed;
                TimeSpan? remaining = speed > 0
                    ? TimeSpan.FromSeconds(Math.Max(0, totalFrames - processed) / speed)
                    : null;
                progress?.Report(new ConversionProgress(
                    ConversionStage.Inferring,
                    Math.Clamp(5 + processed / (double)totalFrames * 92, 5, 97),
                    processed,
                    totalFrames,
                    speed,
                    remaining,
                    $"正在生成深度帧 {processed:N0} / {totalFrames:N0}"));
            }

            if (encoder is null) throw new InvalidOperationException("视频中没有解码出任何画面。");

            await encoder.StandardInput.BaseStream.FlushAsync(cancellationToken);
            encoder.StandardInput.Close();
            progress?.Report(new ConversionProgress(ConversionStage.Finalizing, 98, processed, totalFrames, 0, null, "正在写入视频索引和音频"));
            await decoder.WaitForExitAsync(cancellationToken);
            await encoder.WaitForExitAsync(cancellationToken);
            await decoderErrorTask;
            if (encoderErrorTask is not null) await encoderErrorTask;

            if (decoder.ExitCode != 0) throw new InvalidOperationException("FFmpeg 视频解码失败。");
            if (encoder.ExitCode != 0 || !File.Exists(partialPath))
            {
                throw new InvalidOperationException("FFmpeg 视频编码失败，请查看处理日志。");
            }

            File.Move(partialPath, settings.OutputPath, true);
            progress?.Report(new ConversionProgress(ConversionStage.Completed, 100, processed, totalFrames, 0, TimeSpan.Zero, "转换完成"));
        }
        catch
        {
            Kill(decoder);
            if (encoder is not null) Kill(encoder);
            TryDelete(partialPath);
            throw;
        }
        finally
        {
            encoder?.Dispose();
        }
    }

    private Process StartDecoder(string inputPath, int width, int height, double framesPerSecond)
    {
        var info = CreateStartInfo();
        AddArguments(info, "-hide_banner", "-nostdin", "-loglevel", "warning", "-i", inputPath, "-map", "0:v:0", "-an",
            "-vf", $"scale={width}:{height}:flags=lanczos", "-r", framesPerSecond.ToString("0.########", CultureInfo.InvariantCulture),
            "-fps_mode", "cfr", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1");
        info.RedirectStandardOutput = true;
        return Process.Start(info) ?? throw new InvalidOperationException("无法启动 FFmpeg 解码器。");
    }

    private Process StartEncoder(string inputPath, string outputPath, int depthWidth, int depthHeight, VideoMetadata metadata, string encoderName)
    {
        var info = CreateStartInfo();
        info.RedirectStandardInput = true;
        var fps = metadata.FramesPerSecond.ToString("0.########", CultureInfo.InvariantCulture);
        AddArguments(info,
            "-hide_banner", "-nostdin", "-loglevel", "warning", "-y",
            "-f", "rawvideo", "-pix_fmt", "gray", "-video_size", $"{depthWidth}x{depthHeight}", "-framerate", fps, "-i", "pipe:0",
            "-i", inputPath, "-map", "0:v:0", "-map", "1:a?",
            "-vf", $"scale={metadata.Width}:{metadata.Height}:flags=lanczos,format=yuv420p", "-c:v", encoderName);
        switch (encoderName)
        {
            case "h264_nvenc":
                AddArguments(info, "-preset", "p4", "-tune", "hq", "-rc", "vbr", "-cq", "19", "-b:v", "0");
                break;
            case "h264_qsv":
                AddArguments(info, "-preset", "medium", "-global_quality", "19");
                break;
            case "h264_amf":
                AddArguments(info, "-quality", "quality", "-rc", "cqp", "-qp_i", "19", "-qp_p", "19");
                break;
            default:
                AddArguments(info, "-preset", "medium", "-crf", "18");
                break;
        }
        AddArguments(info, "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", outputPath);
        return Process.Start(info) ?? throw new InvalidOperationException("无法启动 FFmpeg 编码器。");
    }

    private async Task<string> ResolveEncoderAsync(
        VideoEncoder selected,
        HardwareDevice device,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (selected == VideoEncoder.SoftwareH264) return "libx264";

        var requestedEncoder = selected switch
        {
            VideoEncoder.NvidiaH264 => "h264_nvenc",
            VideoEncoder.IntelH264 => "h264_qsv",
            VideoEncoder.AmdH264 => "h264_amf",
            _ => null,
        };

        var info = CreateStartInfo();
        info.RedirectStandardOutput = true;
        AddArguments(info, "-hide_banner", "-encoders");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法检查 FFmpeg 编码器。");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (requestedEncoder is not null)
        {
            if (!HasEncoder(output, requestedEncoder) || !await CanUseEncoderAsync(requestedEncoder, cancellationToken))
            {
                throw new InvalidOperationException($"当前硬件或 FFmpeg 无法使用 {GetEncoderDisplayName(requestedEncoder)} 编码器。");
            }
            return requestedEncoder;
        }

        foreach (var candidate in GetAutoEncoderOrder(device.Name))
        {
            if (!HasEncoder(output, candidate)) continue;
            if (await CanUseEncoderAsync(candidate, cancellationToken)) return candidate;
            log?.Invoke($"{GetEncoderDisplayName(candidate)} 不可用，正在尝试其他编码器。");
        }

        if (!HasEncoder(output, "libx264"))
        {
            throw new InvalidOperationException("当前 FFmpeg 没有可用的 H.264 编码器。");
        }
        return "libx264";
    }

    private async Task<bool> CanUseEncoderAsync(string encoderName, CancellationToken cancellationToken)
    {
        var info = CreateStartInfo();
        AddArguments(info, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
            "color=c=black:s=256x144:r=1", "-frames:v", "1", "-c:v", encoderName,
            "-pix_fmt", "yuv420p", "-f", "null", "-");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法测试 FFmpeg 编码器。");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }

    private static bool HasEncoder(string encoderList, string encoderName) =>
        encoderList.Contains(encoderName, StringComparison.Ordinal);

    private static IEnumerable<string> GetAutoEncoderOrder(string deviceName)
    {
        if (deviceName.Contains("Intel", StringComparison.OrdinalIgnoreCase)) yield return "h264_qsv";
        if (deviceName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) yield return "h264_amf";
        if (deviceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) yield return "h264_nvenc";

        foreach (var encoder in new[] { "h264_nvenc", "h264_qsv", "h264_amf" })
        {
            var matchesDevice = encoder switch
            {
                "h264_nvenc" => deviceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase),
                "h264_qsv" => deviceName.Contains("Intel", StringComparison.OrdinalIgnoreCase),
                _ => deviceName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                     deviceName.Contains("Radeon", StringComparison.OrdinalIgnoreCase),
            };
            if (!matchesDevice) yield return encoder;
        }
    }

    private static string GetEncoderDisplayName(string encoderName) => encoderName switch
    {
        "h264_nvenc" => "NVIDIA NVENC H.264",
        "h264_qsv" => "Intel Quick Sync H.264",
        "h264_amf" => "AMD AMF H.264",
        _ => encoderName,
    };

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

    private static async Task<bool> ReadFrameAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException("视频帧数据不完整。");
            offset += read;
        }
        return true;
    }

    private static async Task DrainErrorAsync(Process process, Action<string>? log, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (!string.IsNullOrWhiteSpace(line)) log?.Invoke(line);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
