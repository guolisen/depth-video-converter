param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $workspaceRoot "src\DepthVideo.App\DepthVideo.App.csproj"
$publishPath = Join-Path $workspaceRoot "artifacts\$Runtime"

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishPath

$ffmpegCommand = Get-Command ffmpeg -ErrorAction Stop
$ffprobeCommand = Get-Command ffprobe -ErrorAction Stop
$ffmpegDirectory = Split-Path -Parent $ffmpegCommand.Source
$toolsPath = Join-Path $publishPath "tools\ffmpeg"
New-Item -ItemType Directory -Path $toolsPath -Force | Out-Null

Get-ChildItem -LiteralPath $ffmpegDirectory -File | Where-Object {
    $_.Extension -in @(".exe", ".dll")
} | Copy-Item -Destination $toolsPath -Force

if (-not (Test-Path (Join-Path $toolsPath "ffmpeg.exe")) -or
    -not (Test-Path (Join-Path $toolsPath "ffprobe.exe"))) {
    throw "FFmpeg or FFprobe was not copied to the portable build."
}

Write-Host "Portable build created at $publishPath"
