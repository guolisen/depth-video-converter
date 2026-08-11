using System.Management;
using DepthVideo.Core.Models;

namespace DepthVideo.Core.Services;

public sealed class HardwareDetector
{
    public IReadOnlyList<HardwareDevice> Detect()
    {
        var devices = new List<HardwareDevice>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                var index = 0;
                foreach (ManagementObject item in searcher.Get())
                {
                    var name = item["Name"]?.ToString() ?? $"GPU {index}";
                    var isNvidia = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
                    var detail = isNvidia ? "DirectML · 高性能 GPU" : "DirectML";
                    devices.Add(new HardwareDevice($"dml-{index}", name, ComputeBackend.DirectMl, index, isNvidia, detail));
                    index++;
                }
            }
            catch (ManagementException)
            {
                // CPU fallback is always available.
            }
        }

        devices.Add(new HardwareDevice("cpu", "CPU 兼容模式", ComputeBackend.Cpu, 0, false, "速度较慢"));
        return devices.OrderByDescending(device => device.IsHighPerformance)
            .ThenBy(device => device.Backend == ComputeBackend.Cpu)
            .ToArray();
    }
}
