namespace DepthVideo.Core.Models;

public enum ComputeBackend
{
    DirectMl,
    Cpu,
}

public sealed record HardwareDevice(
    string Id,
    string Name,
    ComputeBackend Backend,
    int DeviceIndex,
    bool IsHighPerformance,
    string Detail)
{
    public string DisplayName => IsHighPerformance ? $"{Name}  推荐" : Name;
}
