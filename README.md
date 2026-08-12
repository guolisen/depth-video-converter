# Depth Video Converter

Windows desktop application that converts regular videos into grayscale relative-depth videos locally.
一款 Windows 桌面应用程序，可在本地将普通视频转换为灰度相对深度视频。

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

## UI
<img width="1060" height="751" alt="屏幕截图 2026-08-11 203607" src="https://github.com/user-attachments/assets/e09d9a78-e3d5-4a37-9a21-fd396817c9c8" />

## Result
Depth video:
<img width="1231" height="704" alt="屏幕截图 2026-08-11 203139" src="https://github.com/user-attachments/assets/0219c383-358a-4b97-af9a-b24c51e3065d" />



