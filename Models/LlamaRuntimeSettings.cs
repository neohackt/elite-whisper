using System;

namespace EliteWhisper.Models
{
    public class LlamaRuntimeSettings
    {
        public int Threads { get; set; } = Environment.ProcessorCount;
        public int ContextSize { get; set; } = 4096;
        public double Temperature { get; set; } = 0.3;
        public int GpuLayers { get; set; } = 0; // 0 = CPU only
        public double TopP { get; set; } = 0.9;
        public double RepeatPenalty { get; set; } = 1.1;
    }
}
