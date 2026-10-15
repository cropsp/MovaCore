using System.Collections.Generic;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>Which graphics card dictation runs on, and how it decodes there.</summary>
    public static class GpuChoice
    {
        /// <summary>A discrete card with at least this much memory decodes with beam search.</summary>
        public const ulong BeamSearchMemory = 4UL << 30;

        /// <summary>
        /// The card with the chosen name; otherwise (automatic, or the chosen one is gone) the discrete card with the most
        /// memory, since whisper.cpp would take the first one Vulkan lists, which on a laptop is often the integrated one;
        /// otherwise the first integrated card. Null without any.
        /// </summary>
        public static GpuDevice? Pick(IReadOnlyList<GpuDevice> devices, string? name)
        {
            if (name != null)
            {
                foreach (GpuDevice device in devices)
                {
                    if (device.Name == name) return device;
                }
            }

            GpuDevice? best = null;
            foreach (GpuDevice device in devices)
            {
                if (device.Discrete && (best == null || device.Memory > best.Memory)) best = device;
            }
            return best ?? (devices.Count > 0 ? devices[0] : null);
        }

        /// <summary>
        /// Beam search weighs five candidates instead of one: more accurate, and on an RTX 4060 it costs 0.1-0.2 s a
        /// phrase. On an integrated or a small card it would cost more than it is worth, so those keep greedy decoding.
        /// </summary>
        public static bool UseBeamSearch(GpuDevice? device) => device is { Discrete: true } && device.Memory >= BeamSearchMemory;
    }
}
