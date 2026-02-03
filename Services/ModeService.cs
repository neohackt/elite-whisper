using System.Collections.Generic;
using System.Linq;
using EliteWhisper.Models;

namespace EliteWhisper.Services
{
    /// <summary>
    /// Manages dictation modes and tracks the active mode.
    /// </summary>
    public class ModeService
    {
        private readonly WhisperConfigurationService _configService;
        private readonly List<DictationMode> _modes = new();
        private DictationMode? _activeMode;
        
        public IReadOnlyList<DictationMode> Modes => _modes.AsReadOnly();
        
        public DictationMode ActiveMode
        {
            get => _activeMode ?? GetDefaultMode();
            private set => _activeMode = value;
        }
        
        public ModeService(WhisperConfigurationService configService)
        {
            _configService = configService;
            LoadModes();
        }
        
        /// <summary>
        /// Load modes from configuration or create defaults.
        /// </summary>
        private void LoadModes()
        {
            var config = _configService.CurrentConfiguration;
            
            // Load from config if available
            if (config.Modes != null && config.Modes.Count > 0)
            {
                _modes.AddRange(config.Modes);
            }
            else
            {
                // Create default modes
                _modes.AddRange(CreateDefaultModes());
            }
            
            // Set active mode
            if (!string.IsNullOrEmpty(config.ActiveModeId))
            {
                _activeMode = _modes.FirstOrDefault(m => m.Id == config.ActiveModeId);
            }
            
            _activeMode ??= GetDefaultMode();
        }
        
        /// <summary>
        /// Create default dictation modes.
        /// </summary>
        private List<DictationMode> CreateDefaultModes()
        {
            return new List<DictationMode>
            {
                // Default: Cleanup
                new DictationMode
                {
                    Id = "cleanup",
                    Name = "Cleanup",
                    Description = "Fix grammar and punctuation",
                    ModelId = "balanced",
                    AutoPunctuation = true,
                    SmartFormatting = true,
                    EnablePostProcessing = true,
                    IsDefault = true,
                    IsPro = false,
                    PostProcess = new PostProcessProfile
                    {
                        PromptTemplate = "Clean up the {{text}} text for clarity and natural flow while preserving meaning and the original tone. Use informal, plain language unless the {{text}} clearly uses a professional tone; in that case, match it. Fix obvious grammar, remove fillers and stutters, collapse repetitions, and keep names and numbers. Format any lists as proper bullet points or numbered lists. Write numbers as numerals (e.g., 'five' → '5', 'twenty dollars' → '$20'). Keep the original intent and nuance. Organize into short paragraphs of 2–4 sentences for readability. Do not add explanations, labels, metadata, or instructions. Output only the cleaned text.",
                        RunLocally = false,
                        PreferredProvider = PreferredProvider.Auto
                    }
                },
                
                // Email
                new DictationMode
                {
                    Id = "email",
                    Name = "Email",
                    Description = "Professional email formatting",
                    ModelId = "balanced",
                    AutoPunctuation = true,
                    SmartFormatting = true,
                    EnablePostProcessing = true,
                    IsDefault = false,
                    IsPro = false,
                    PostProcess = new PostProcessProfile
                    {
                        PromptTemplate = "Rewrite the text as a professional email.\nConcise, polite, and clear.\n\n{{text}}",
                        RunLocally = false,
                        PreferredProvider = PreferredProvider.Auto
                    }
                },
                
                // Meeting
                new DictationMode
                {
                    Id = "meeting",
                    Name = "Meeting Notes",
                    Description = "Summarize with action items",
                    ModelId = "accurate",
                    AutoPunctuation = true,
                    SmartFormatting = true,
                    EnablePostProcessing = true,
                    IsDefault = false,
                    IsPro = false,
                    PostProcess = new PostProcessProfile
                    {
                        PromptTemplate = "Summarize into bullet points and action items.\n\n{{text}}",
                        RunLocally = false,
                        PreferredProvider = PreferredProvider.Auto
                    }
                },

                // Chat
                new DictationMode
                {
                    Id = "chat",
                    Name = "Chat",
                    Description = "Informal, concise messages",
                    ModelId = "balanced", // Fast or Balanced recommended
                    AutoPunctuation = true,
                    SmartFormatting = true,
                    EnablePostProcessing = true,
                    IsDefault = false,
                    IsPro = false,
                    PostProcess = new PostProcessProfile
                    {
                        PromptTemplate = @"Rewrite the {{text}} text as a chat message that is informal, concise, and conversational.

Lightly fix grammar and punctuation, remove filler words and repeated phrases, and improve flow without changing the original meaning. Preserve the original tone, slang, and intent; do not formalize language unless the {{text}} is already formal.

Keep emotive markers and emojis if present; do not invent new ones. Do not add greetings, sign-offs, explanations, or commentary.

Format the output like a modern chat message:
- Use short lines with natural breaks
- Keep spacing emoji-friendly
- Format any lists as proper bullet points or numbered lists

Write numbers as numerals (e.g., ""five"" → ""5"", ""twenty dollars"" → ""$20"").

Output only the chat message.",
                        RunLocally = false, // Deprecated
                        PreferredProvider = PreferredProvider.Local
                    }
                },
                
                // Raw (no post-processing)
                new DictationMode
                {
                    Id = "raw",
                    Name = "Raw",
                    Description = "No AI enhancement",
                    ModelId = "fast",
                    AutoPunctuation = false,
                    SmartFormatting = false,
                    EnablePostProcessing = false,
                    IsDefault = false,
                    IsPro = false
                }
            };
        }
        
        /// <summary>
        /// Set the active dictation mode.
        /// </summary>
        public void SetActiveMode(DictationMode mode)
        {
            ActiveMode = mode;
            SaveModes();
        }

        /// <summary>
        /// Persist current mode configuration to disk.
        /// </summary>
        public void SaveModes()
        {
            var config = _configService.CurrentConfiguration;
            config.ActiveModeId = ActiveMode.Id;
            // config.Modes is already referenced by _modes, so we just save.
            if (config.Modes == null || config.Modes.Count == 0)
            {
                config.Modes = new List<DictationMode>(_modes);
            }
            _configService.SaveConfiguration(config);
        }
        
        /// <summary>
        /// Get the default mode.
        /// </summary>
        private DictationMode GetDefaultMode()
        {
            return _modes.FirstOrDefault(m => m.IsDefault) ?? _modes.FirstOrDefault() ?? CreateDefaultModes()[0];
        }
    }
}
