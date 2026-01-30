using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    public class ModelRegistryService
    {
        private List<AIModelRegistryEntry> _availableModels = new();
        public IReadOnlyList<AIModelRegistryEntry> AvailableModels => _availableModels.AsReadOnly();

        public ModelRegistryService()
        {
            LoadRegistry();
        }

        private void LoadRegistry()
        {
            try
            {
                // Should match the path from CopyToOutputDirectory
                string registryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "models.json");
                
                if (File.Exists(registryPath))
                {
                    string json = File.ReadAllText(registryPath);
                    var models = JsonSerializer.Deserialize<List<AIModelRegistryEntry>>(json);
                    
                    if (models != null)
                    {
                        _availableModels = models;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Model registry not found at: {registryPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load model registry: {ex.Message}");
            }
        }

        public AIModelRegistryEntry? GetModelById(string id)
        {
            return _availableModels.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Get the recommended model from the registry
        /// </summary>
        public AIModelRegistryEntry? GetRecommendedModel()
        {
            return _availableModels.FirstOrDefault(m => m.Recommended);
        }
    }
}
