using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EliteWhisper.Models;
using System.Windows.Media;
using System;
using System.Windows;

namespace EliteWhisper.ViewModels
{
    public partial class ModelCardViewModel : ObservableObject
    {
        private readonly AIModelRegistryEntry _registryEntry;
        
        [ObservableProperty]
        private bool _isInstalled;
        
        [ObservableProperty]
        private bool _isActive;
        
        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private bool _isLoading;
        
        [ObservableProperty]
        private double _downloadProgress;

        public ModelCardViewModel(AIModelRegistryEntry entry)
        {
            _registryEntry = entry;
        }

        public string Id => _registryEntry.Id;
        public string Name => _registryEntry.Name;
        public string Description => _registryEntry.Description;
        public string Filename => _registryEntry.Filename;
        public int SizeMB => _registryEntry.SizeMB;
        public string Tier => _registryEntry.Tier;
        public string DownloadUrl => _registryEntry.DownloadUrl;
        public bool Recommended => _registryEntry.Recommended;
        public string? Sha256 => _registryEntry.Sha256;
        
        // Visual Helpers
        public int SpeedRating => _registryEntry.SpeedRating;
        public int AccuracyRating => _registryEntry.AccuracyRating;

        public string SpeedLabel => SpeedRating switch
        {
            3 => "Fast",
            2 => "Medium",
            1 => "Slow",
            _ => "Unknown"
        };

        public string AccuracyLabel => AccuracyRating switch
        {
            3 => "High",
            2 => "Medium",
            1 => "Low",
            _ => "Unknown"
        };

        // Colors based on Tier
        public string AccentColor => Id.ToLower() switch
        {
            "fast" => "#D97706", // Amber
            "accurate" => "#16A34A", // Green
            _ => "#6366F1" // Indigo (Balanced)
        };

        public string BackgroundColor => Id.ToLower() switch
        {
            "fast" => "#FEF3C7",
            "accurate" => "#DCFCE7",
            _ => "#E0E7FF"
        };
        
        public string IconPath => Id.ToLower() switch
        {
            "fast" => "IconLightningGeometry",
            "accurate" => "IconTargetGeometry",
            _ => "IconScalesGeometry"
        };
    }
}
