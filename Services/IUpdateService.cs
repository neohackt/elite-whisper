using System;
using System.Threading.Tasks;

namespace EliteWhisper.Services
{
    public interface IUpdateService
    {
        /// <summary>
        /// True only when a trusted release-signing public key is configured, i.e. when a
        /// downloaded update can be signature-verified before it is installed. When false,
        /// remote update installation is disabled and no unsigned update can be accepted.
        /// </summary>
        bool RemoteUpdatesEnabled { get; }

        void Start();
        Task CheckForUpdatesAsync(); // Changed to Task for async UI feedback
    }
}
