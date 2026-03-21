using System;
using System.Runtime.InteropServices;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EliteWhisper.ViewModels
{
    public partial class AboutViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _appVersion;

        [ObservableProperty]
        private string _osVersion;

        [ObservableProperty]
        private string _dotNetVersion;

        [ObservableProperty]
        private string _architecture;

        [ObservableProperty]
        private string _licenseType;

        [ObservableProperty]
        private string _licenseDescription;

        public AboutViewModel()
        {
            // App Version
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            _appVersion = $"Version {version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 1}";

            // System Information
            _osVersion = GetFriendlyOSName();
            _dotNetVersion = RuntimeInformation.FrameworkDescription;
            _architecture = RuntimeInformation.ProcessArchitecture.ToString();

            // License Information
            _licenseType = "Commercial License (Annual Subscription)";
            _licenseDescription = "Elite Whisper is licensed under a Commercial End User License Agreement (EULA) with an annual subscription fee. By using this software, you agree to the terms of the EULA. Third-party components are used under their respective open-source licenses (MIT/Apache).";
        }

        private string GetFriendlyOSName()
        {
            try
            {
                // RuntimeInformation.OSDescription usually returns something like "Microsoft Windows 10.0.22631"
                // For Windows 11 it often still says 10.0 but with a high build number.
                string desc = RuntimeInformation.OSDescription;
                if (desc.Contains("Windows"))
                {
                    if (desc.Contains("10.0.22") || desc.Contains("10.0.26")) return desc.Replace("Windows 10", "Windows 11");
                }
                return desc;
            }
            catch
            {
                return "Windows (Unknown Version)";
            }
        }
    }
}
