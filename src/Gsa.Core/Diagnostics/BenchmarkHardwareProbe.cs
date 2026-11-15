using System.Globalization;
using System.Runtime.InteropServices;

namespace Gsa.Core.Diagnostics;

/// <summary>Inspects real host hardware/OS metadata without fabricating reference hardware when absent.</summary>
public static class BenchmarkHardwareProbe
{
    public const string ReferenceSpecSummary =
        "AMD Radeon Vega 7 (integrated GPU), 6-core CPU, 16 GiB dual-channel RAM, SSD, no discrete GPU, 1920x1080 Compatibility";

    public static BenchmarkHardwareInfo DetectHost(
        string rendererBackend = "Compatibility",
        bool vsyncEnabled = false,
        string targetResolution = "1920x1080",
        string? gpuModelOverride = null,
        string? gpuDriverOverride = null,
        string? cpuModelOverride = null,
        bool headless = true)
    {
        var osDesc = RuntimeInformation.OSDescription.Trim();
        var arch = RuntimeInformation.OSArchitecture.ToString();
        var logicalCores = Environment.ProcessorCount;
        int? physicalCores = null;
        var cpuModel = cpuModelOverride;
        double? ramGb = null;
        var gpuModel = gpuModelOverride;
        var gpuDriver = gpuDriverOverride;
        var hasDiscreteGpu = false;
        var storageType = "unknown";

        if (OperatingSystem.IsLinux())
        {
            (cpuModel, physicalCores) = ReadLinuxCpuInfo(cpuModel);
            ramGb = ReadLinuxRamGb();
            var (detectedGpu, detectedDriver, detectedDiscrete) = ReadLinuxDrmGpuInfo();
            gpuModel ??= detectedGpu;
            gpuDriver ??= detectedDriver;
            hasDiscreteGpu = detectedDiscrete;
            storageType = DetectLinuxStorageType();
        }
        else
        {
            var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (available > 0)
                ramGb = Math.Round(available / (1024.0 * 1024.0 * 1024.0), 1);
            cpuModel ??= Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? $"{arch} CPU";
        }

        cpuModel ??= $"{arch} CPU ({logicalCores} logical cores)";
        gpuModel ??= headless ? "headless_no_graphical_adapter" : "unknown_gpu";
        gpuDriver ??= headless ? "headless_no_driver" : "unknown_driver";

        var ramChannels = "unknown";
        var matched = EvaluateReferenceHardwareMatch(
            cpuPhysicalCores: physicalCores,
            cpuLogicalCores: logicalCores,
            ramTotalGb: ramGb,
            ramChannels: ramChannels,
            gpuModel: gpuModel,
            gpuDriver: gpuDriver,
            hasDiscreteGpu: hasDiscreteGpu,
            headless: headless);

        return new BenchmarkHardwareInfo(
            OsDescription: osDesc,
            Architecture: arch,
            CpuModel: cpuModel,
            CpuLogicalCores: logicalCores,
            CpuPhysicalCores: physicalCores,
            GpuModel: gpuModel,
            GpuDriver: gpuDriver,
            HasDiscreteGpu: hasDiscreteGpu,
            RamTotalGb: ramGb,
            RamChannels: ramChannels,
            StorageType: storageType,
            TargetResolution: targetResolution,
            RendererBackend: rendererBackend,
            VsyncEnabled: vsyncEnabled,
            ReferenceHardwareMatched: matched,
            ReferenceTargetSummary: ReferenceSpecSummary);
    }

    public static bool EvaluateReferenceHardwareMatch(
        int? cpuPhysicalCores,
        int cpuLogicalCores,
        double? ramTotalGb,
        string ramChannels,
        string gpuModel,
        string gpuDriver,
        bool hasDiscreteGpu,
        bool headless)
    {
        if (headless || hasDiscreteGpu)
            return false;
        if (string.IsNullOrWhiteSpace(gpuModel) || string.IsNullOrWhiteSpace(gpuDriver))
            return false;
        if (gpuModel.Contains("headless", StringComparison.OrdinalIgnoreCase) ||
            gpuModel.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
            gpuDriver.Contains("headless", StringComparison.OrdinalIgnoreCase) ||
            gpuDriver.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var coresOk = cpuPhysicalCores == 6 || (!cpuPhysicalCores.HasValue && cpuLogicalCores is >= 6 and <= 12);
        var ramOk = ramTotalGb is >= 14.0 and <= 18.0;
        var channelsOk = string.Equals(ramChannels, "dual", StringComparison.OrdinalIgnoreCase);
        var gpuOk = gpuModel.Contains("Vega 7", StringComparison.OrdinalIgnoreCase) ||
                    (gpuModel.Contains("Radeon", StringComparison.OrdinalIgnoreCase) &&
                     gpuModel.Contains("Graphics", StringComparison.OrdinalIgnoreCase));

        return coresOk && ramOk && channelsOk && gpuOk;
    }

    private static (string? Model, int? PhysicalCores) ReadLinuxCpuInfo(string? overrideModel)
    {
        try
        {
            if (!File.Exists("/proc/cpuinfo"))
                return (overrideModel, null);

            string? model = overrideModel;
            int? cores = null;
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (model is null && line.StartsWith("model name", StringComparison.Ordinal))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0)
                        model = line[(idx + 1)..].Trim();
                }
                else if (cores is null && line.StartsWith("cpu cores", StringComparison.Ordinal))
                {
                    var idx = line.IndexOf(':');
                    if (idx >= 0 && int.TryParse(line[(idx + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        cores = parsed;
                }
                if (model is not null && cores is not null)
                    break;
            }
            return (model, cores);
        }
        catch
        {
            return (overrideModel, null);
        }
    }

    private static double? ReadLinuxRamGb()
    {
        try
        {
            if (!File.Exists("/proc/meminfo"))
                return null;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var kb))
                {
                    return Math.Round(kb / (1024.0 * 1024.0), 1);
                }
            }
        }
        catch
        {
            // Ignore unreadable procfs on locked-down containers.
        }
        return null;
    }

    private static (string? GpuModel, string? GpuDriver, bool DiscreteGpu) ReadLinuxDrmGpuInfo()
    {
        try
        {
            const string drmDir = "/sys/class/drm";
            if (!Directory.Exists(drmDir))
                return (null, null, false);

            var vendors = new List<string>();
            var discrete = false;
            foreach (var cardDir in Directory.GetDirectories(drmDir, "card[0-9]*"))
            {
                var vendorPath = Path.Combine(cardDir, "device", "vendor");
                var devicePath = Path.Combine(cardDir, "device", "device");
                if (!File.Exists(vendorPath))
                    continue;

                var vendor = File.ReadAllText(vendorPath).Trim();
                var device = File.Exists(devicePath) ? File.ReadAllText(devicePath).Trim() : "unknown";
                if (string.Equals(vendor, "0x10de", StringComparison.OrdinalIgnoreCase))
                    discrete = true;
                vendors.Add($"PCI {vendor}:{device}");
            }

            if (vendors.Count == 0)
                return (null, null, false);

            return (string.Join(", ", vendors.Distinct(StringComparer.Ordinal)), "linux_drm", discrete);
        }
        catch
        {
            return (null, null, false);
        }
    }

    private static string DetectLinuxStorageType()
    {
        try
        {
            const string blockDir = "/sys/block";
            if (!Directory.Exists(blockDir))
                return "unknown";
            foreach (var dev in Directory.GetDirectories(blockDir))
            {
                var name = Path.GetFileName(dev);
                if (name.StartsWith("loop", StringComparison.Ordinal) ||
                    name.StartsWith("ram", StringComparison.Ordinal))
                    continue;
                var rotPath = Path.Combine(dev, "queue", "rotational");
                if (!File.Exists(rotPath))
                    continue;
                var rot = File.ReadAllText(rotPath).Trim();
                if (rot == "0")
                    return name.StartsWith("nvme", StringComparison.Ordinal) ? "NVMe SSD" : "SSD";
                if (rot == "1")
                    return "HDD";
            }
        }
        catch
        {
            // Ignore sysfs read errors cleanly.
        }
        return "unknown";
    }
}

