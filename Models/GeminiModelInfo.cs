namespace EliteWhisper.Models
{
    public class GeminiModelInfo
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public int InputTokenLimit { get; set; }
        public int OutputTokenLimit { get; set; }
        public bool SupportsThinking { get; set; }
    }
}
