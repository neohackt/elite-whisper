using System.Threading;
using System.Threading.Tasks;

namespace EliteWhisper.Services.Speech
{
    public interface ISpeechEngine
    {
        string Name { get; }
        bool IsAvailable { get; }
        Task<string> TranscribeAsync(float[] audioSamples, CancellationToken ct);
    }
}
