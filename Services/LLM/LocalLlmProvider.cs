using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using EliteWhisper.Models;

namespace EliteWhisper.Services.LLM
{
    public class LocalLlmProvider : ILlmProvider
    {
        private readonly LlamaCppService _llamaService;
        private readonly LocalModelService _modelService;
        
        public string Name => "Local (Built-in)";

        public bool IsAvailable => _modelService.GetInstalledModels().Any();

        public LocalLlmProvider(LlamaCppService llamaService, LocalModelService modelService)
        {
            _llamaService = llamaService;
            _modelService = modelService;
        }

        private string? _preferredModelName;

        public void SetPreferredModel(string? modelName)
        {
            _preferredModelName = modelName;
        }

        public async Task<string> GenerateAsync(string prompt, LlmOptions options, CancellationToken cancellationToken)
        {
             var models = _modelService.GetInstalledModels();
             if (models.Count == 0)
             {
                 throw new System.InvalidOperationException("No local models installed. Please download a model from the Local Models tab.");
             }

             string? modelPath = null;
             
             if (!string.IsNullOrEmpty(_preferredModelName))
             {
                 var specific = models.FirstOrDefault(m => m.Name.Equals(_preferredModelName, System.StringComparison.OrdinalIgnoreCase));
                 modelPath = specific?.FilePath;
             }
             
             // Fallback to first if not found or not set
             if (string.IsNullOrEmpty(modelPath))
             {
                 modelPath = models.FirstOrDefault()?.FilePath;
             }
             
             if (string.IsNullOrEmpty(modelPath))
             {
                  throw new System.InvalidOperationException("Model path invalid.");
             }

             return await _llamaService.GenerateAsync(modelPath, prompt, options, cancellationToken);
        }
    }
}
