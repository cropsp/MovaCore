using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// The graphics cards Vulkan offers, read through vulkan-1.dll (it comes with the graphics driver) as ggml-vulkan
    /// lists them: discrete and integrated cards, in Vulkan's order. Read once, since cards do not come and go while the
    /// app runs. Empty without Vulkan, on ARM (Whisper.net has no Vulkan build for it) or on failure, which is logged.
    /// </summary>
    internal static unsafe class VulkanDevices
    {
        private const string Library = "vulkan-1.dll";

        // vulkan_core.h on a 64-bit target: VkInstanceCreateInfo, VkPhysicalDeviceProperties, VkPhysicalDeviceMemoryProperties
        private const int InstanceCreateInfoSize = 64;
        private const int StructureTypeInstanceCreateInfo = 1;
        private const int PropertiesSize = 824;
        private const int DeviceTypeOffset = 16;
        private const int DeviceNameOffset = 20;
        private const int DeviceNameLength = 256;
        private const int MemoryPropertiesSize = 520;
        private const int HeapCountOffset = 260;
        private const int HeapsOffset = 264;
        private const int HeapSize = 16; // VkDeviceSize size, VkMemoryHeapFlags flags, padding
        private const int MaxHeaps = 16;
        private const uint HeapDeviceLocal = 1;
        private const int TypeIntegrated = 1;
        private const int TypeDiscrete = 2;

        private static readonly Lazy<IReadOnlyList<GpuDevice>> Devices = new(Read);

        public static IReadOnlyList<GpuDevice> List() => Devices.Value;

        /// <summary>
        /// Implicit Vulkan layers (the overlays of OBS, Steam, RTSS and the like) load into every Vulkan process and have
        /// crashed speech recognition in other apps (Handy). Called before Vulkan is touched; a value the user set is kept.
        /// </summary>
        public static void DisableImplicitLayers()
        {
            if (Environment.GetEnvironmentVariable("VK_LOADER_LAYERS_DISABLE") == null)
                Environment.SetEnvironmentVariable("VK_LOADER_LAYERS_DISABLE", "~implicit~");
        }

        private static IReadOnlyList<GpuDevice> Read()
        {
            var devices = new List<GpuDevice>();
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 || !NativeLibrary.TryLoad(Library, out IntPtr library))
                return devices;

            DisableImplicitLayers();
            try
            {
                var createInstance = (delegate* unmanaged[Stdcall]<byte*, IntPtr, IntPtr*, int>)
                    NativeLibrary.GetExport(library, "vkCreateInstance");
                var destroyInstance = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)
                    NativeLibrary.GetExport(library, "vkDestroyInstance");
                var enumerateDevices = (delegate* unmanaged[Stdcall]<IntPtr, uint*, IntPtr*, int>)
                    NativeLibrary.GetExport(library, "vkEnumeratePhysicalDevices");
                var getProperties = (delegate* unmanaged[Stdcall]<IntPtr, byte*, void>)
                    NativeLibrary.GetExport(library, "vkGetPhysicalDeviceProperties");
                var getMemoryProperties = (delegate* unmanaged[Stdcall]<IntPtr, byte*, void>)
                    NativeLibrary.GetExport(library, "vkGetPhysicalDeviceMemoryProperties");

                // No application info: Vulkan 1.0, which is all these calls need
                byte* createInfo = stackalloc byte[InstanceCreateInfoSize];
                new Span<byte>(createInfo, InstanceCreateInfoSize).Clear();
                *(int*)createInfo = StructureTypeInstanceCreateInfo;
                IntPtr instance;
                int result = createInstance(createInfo, IntPtr.Zero, &instance);
                if (result != 0)
                {
                    AppLog.Info($"Graphics cards: Vulkan is not available (error {result})");
                    return devices;
                }

                try
                {
                    uint count = 0;
                    enumerateDevices(instance, &count, null);
                    var handles = new IntPtr[count];
                    fixed (IntPtr* handle = handles)
                        enumerateDevices(instance, &count, handle);

                    byte* properties = stackalloc byte[PropertiesSize];
                    byte* memory = stackalloc byte[MemoryPropertiesSize];
                    for (int i = 0; i < count; i++)
                    {
                        getProperties(handles[i], properties);
                        int type = *(int*)(properties + DeviceTypeOffset);
                        if (type != TypeDiscrete && type != TypeIntegrated) continue;

                        var name = new ReadOnlySpan<byte>(properties + DeviceNameOffset, DeviceNameLength);
                        int end = name.IndexOf((byte)0);
                        getMemoryProperties(handles[i], memory);
                        devices.Add(new GpuDevice(
                            devices.Count,
                            Encoding.UTF8.GetString(end >= 0 ? name[..end] : name).Trim(),
                            type == TypeDiscrete,
                            LargestDeviceLocalHeap(memory)));
                    }
                }
                finally
                {
                    destroyInstance(instance, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                AppLog.Info($"Graphics cards: could not list them ({ex.Message})");
                return devices;
            }

            AppLog.Info("Graphics cards: " + (devices.Count == 0 ? "none" : string.Join("; ", devices.ConvertAll(Describe))));
            return devices;
        }

        private static ulong LargestDeviceLocalHeap(byte* memory)
        {
            uint heaps = Math.Min(*(uint*)(memory + HeapCountOffset), MaxHeaps);
            ulong largest = 0;
            for (int i = 0; i < heaps; i++)
            {
                byte* heap = memory + HeapsOffset + i * HeapSize;
                if ((*(uint*)(heap + 8) & HeapDeviceLocal) != 0) largest = Math.Max(largest, *(ulong*)heap);
            }
            return largest;
        }

        private static string Describe(GpuDevice device) =>
            string.Format(CultureInfo.InvariantCulture, "{0} ({1}, {2:0.0} GB)",
                device.Name, device.Discrete ? "discrete" : "integrated", device.Memory / (double)(1UL << 30));
    }
}
