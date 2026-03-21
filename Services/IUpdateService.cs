using System;
using System.Threading.Tasks;

namespace EliteWhisper.Services
{
    public interface IUpdateService
    {
        void Start();
        Task CheckForUpdatesAsync(); // Changed to Task for async UI feedback
    }
}
