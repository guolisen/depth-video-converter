using DepthVideo.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DepthVideo.Core.Inference;

public sealed class OnnxDepthEstimator : IDepthEstimator
{
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;

    public OnnxDepthEstimator(string modelPath, HardwareDevice device)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("找不到深度模型。", modelPath);
        }

        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (device.Backend == ComputeBackend.DirectMl)
        {
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            options.EnableMemoryPattern = false;
            options.AppendExecutionProvider_DML(device.DeviceIndex);
        }

        _session = new InferenceSession(modelPath, options);
        _inputName = _session.InputMetadata.Keys.Single();
        _outputName = _session.OutputMetadata.Keys.First();
    }

    public DepthPrediction Estimate(ReadOnlySpan<byte> rgb24, int width, int height)
    {
        var pixelCount = checked(width * height);
        if (rgb24.Length != pixelCount * 3)
        {
            throw new ArgumentException("RGB frame size does not match its dimensions.", nameof(rgb24));
        }

        var input = new DenseTensor<float>([1, 3, height, width]);
        var target = input.Buffer.Span;
        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var source = pixel * 3;
            target[pixel] = (rgb24[source] / 255f - Mean[0]) / Std[0];
            target[pixelCount + pixel] = (rgb24[source + 1] / 255f - Mean[1]) / Std[1];
            target[pixelCount * 2 + pixel] = (rgb24[source + 2] / 255f - Mean[2]) / Std[2];
        }

        var inputValue = NamedOnnxValue.CreateFromTensor(_inputName, input);
        using var results = _session.Run([inputValue], [_outputName]);
        var output = results.Single().AsTensor<float>();
        var dimensions = output.Dimensions.ToArray();
        return new DepthPrediction(output.ToArray(), dimensions[^1], dimensions[^2]);
    }

    public void Dispose() => _session.Dispose();
}
