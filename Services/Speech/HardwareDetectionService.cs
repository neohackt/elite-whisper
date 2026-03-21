using System;
using System.Management;

namespace EliteWhisper.Services.Speech
{
    public class HardwareProfile
    {
        public int CpuCores { get; set; }
        public bool HasGpu { get; set; }
        public bool HasNvidiaGpu { get; set; }
        public long TotalRamBytes { get; set; }
    }

    public class HardwareDetectionService
    {
        private HardwareProfile? _cachedProfile;

        public HardwareProfile GetProfile()
        {
            if (_cachedProfile != null) return _cachedProfile;

            var profile = new HardwareProfile
            {
                CpuCores = Environment.ProcessorCount,
                HasNvidiaGpu = false,
                HasGpu = false,
                TotalRamBytes = GetTotalRam()
            };

            DetectGpu(profile);

            _cachedProfile = profile;
            return _cachedProfile;
        }

        private void DetectGpu(HardwareProfile profile)
        {
            try
            {
#pragma warning disable CA1416 // Validate platform compatibility
                using var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.ToLowerInvariant();
                    if (name != null)
                    {
                        profile.HasGpu = true;
                        
                        // Example broad check for NVIDIA cards (GeForce, Quadro, Tesla, etc)
                        if (name.Contains("nvidia") || name.Contains("geforce") || name.Contains("quadro") || name.Contains("rtx") || name.Contains("gtx"))
                        {
                            profile.HasNvidiaGpu = true;
                            // Found NVIDIA, stop searching
                            break;
                        }
                    }
                }
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting GPU via WMI: {ex.Message}");
                // Fallback assumptions based on env vars could be placed here if needed
            }
        }

        private long GetTotalRam()
        {
            try
            {
#pragma warning disable CA1416
                using var searcher = new ManagementObjectSearcher("select TotalVisibleMemorySize from Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["TotalVisibleMemorySize"] is ulong sizeKB)
                    {
                        return (long)(sizeKB * 1024);
                    }
                }
#pragma warning restore CA1416
            }
            catch
            {
                // Ignore
            }
            
            return 8L * 1024 * 1024 * 1024; // Fallback 8GB
        }
    }
}
