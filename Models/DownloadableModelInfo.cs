using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace EliteWhisper.Models
{
    public partial class DownloadableModelInfo : ObservableObject
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public double SizeGb { get; set; }
        public int RecommendedRamGb { get; set; }
        public List<string> Tags { get; set; } = new();
        
        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _status = "Ready"; // Ready, Downloading, Installed
        
        // Helper to construct download URL
        public string DownloadUrl => $"https://huggingface.co/{Repo}/resolve/main/{FileName}?download=true";
    }
}
