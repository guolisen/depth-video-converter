# Depth Video Converter

Windows desktop application that converts regular videos into grayscale relative-depth videos locally.

## Current MVP

- WPF desktop UI with drag and drop
- NVIDIA GPU preference through ONNX Runtime DirectML
- FP16 GPU model and Q8 CPU fallback model
- FFmpeg streaming decode and encode
- NVENC H.264 with software H.264 fallback
- Original audio transcoded to AAC
- Progress, remaining time, logs, and cancellation
- Near-white/far-black and inverted output

## Build

```powershell
dotnet build DepthVideoConverter.sln -c Release
dotnet test tests\DepthVideo.Core.Tests\DepthVideo.Core.Tests.csproj -c Release
```

Run the development build:

```powershell
dotnet run --project src\DepthVideo.App\DepthVideo.App.csproj -c Release
```

## Portable publish

```powershell
.\scripts\publish-windows.ps1
```

The output is written to `artifacts\win-x64`. The publish script copies the FFmpeg binaries found on the current PATH, including DLLs from the same directory.

## Processing pipeline

```text
FFmpeg RGB frames -> ONNX depth inference -> temporal range stabilization
-> grayscale frames -> FFmpeg NVENC/libx264 -> MP4 with AAC audio
```

Depth Anything V2 produces relative depth rather than calibrated metric distance.
