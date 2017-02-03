﻿using System;
using System.Diagnostics;
using System.Linq;
using VulkanCore.Ext;
using VulkanCore.Khr;

namespace VulkanCore.Samples
{
    public abstract class VulkanApp : IDisposable
    {
        private readonly IntPtr _hInstance;

        protected VulkanApp(IntPtr hInstance, IWindow window)
        {
            _hInstance = hInstance;
            Window = window;
        }

        protected IWindow Window { get; }

        protected Instance Instance { get; private set; }
        protected DebugReportCallbackExt DebugCallback { get; private set; }
        protected PhysicalDevice PhysicalDevice { get; private set; }
        protected Device Device { get; private set; }

        protected SurfaceKhr Surface { get; private set; }
        protected SwapchainKhr Swapchain { get; private set; }
        protected Image[] SwapchainImages { get; private set; }

        protected Queue GraphicsQueue { get; private set; }
        protected Queue PresentQueue { get; private set; }
        protected CommandPool CommandPool { get; private set; }
        protected CommandBuffer[] CommandBuffers { get; private set; }
        protected Semaphore ImageAvailableSemaphore { get; private set; }
        protected Semaphore RenderingFinishedSemaphore { get; private set; }        

        public virtual void Initialize()
        {
            Window.Initialize(OnResized);

            CreateInstanceAndSurface();
            CreateDeviceAndGetQueues();
            CreateSwapchain();
            CreateSemaphoresAndCommandBuffers();            
        }

        public virtual void Run() => Window.Run(Update, Draw);

        protected virtual void OnResized()
        {
            Device.WaitIdle();
            Swapchain.Dispose();
            CreateSwapchain();
            CommandPool.Reset(); // Resets all the command buffers allocated from the pool.
        }
        protected virtual void Update(Timer timer) { }
        protected virtual void Draw(Timer timer) { }

        public virtual void Dispose()
        {
            Device.WaitIdle();           
            ImageAvailableSemaphore.Dispose();
            RenderingFinishedSemaphore.Dispose();
            CommandPool.Dispose();
            Swapchain.Dispose();
            Device.Dispose();
            Surface.Dispose();
            DebugCallback?.Dispose();
            Instance.Dispose();
        }

        private void CreateInstanceAndSurface()
        {
            // Specify standard validation layers.
            Instance = new Instance(new InstanceCreateInfo(
#if DEBUG
                enabledLayerNames: new[] { Layers.LunarGStandardValidation },
#endif
                enabledExtensionNames: new[] 
                {
                    Extensions.KhrSurface,
                    Extensions.KhrWin32Surface,
#if DEBUG
                    Extensions.ExtDebugReport
#endif
                }
            ));

#if DEBUG
            // Attach debug callback.
            var debugReportCreateInfo = new DebugReportCallbackCreateInfoExt(
                DebugReportFlagsExt.All,
                args =>
                {
                    Trace.WriteLine($"[{args.Flags}][{args.LayerPrefix}] {args.Message}");
                    return args.Flags.HasFlag(DebugReportFlagsExt.Error);
                }
            );
            DebugCallback = Instance.CreateDebugReportCallbackExt(debugReportCreateInfo);
#endif

            // Create surface.
            Surface = Instance.CreateWin32SurfaceKhr( // TODO: x-plat
                new Win32SurfaceCreateInfoKhr(_hInstance, Window.Handle));
        }

        private void CreateDeviceAndGetQueues()
        {
            // Get physical device.
            int graphicsQueueFamilyIndex = -1;
            int presentQueueFamilyIndex = -1;
            foreach (PhysicalDevice physicalDevice in Instance.EnumeratePhysicalDevices())
            {
                QueueFamilyProperties[] queueFamilyProperties = physicalDevice.GetQueueFamilyProperties();
                for (int i = 0; i < queueFamilyProperties.Length; i++)
                {
                    if (queueFamilyProperties[i].QueueFlags.HasFlag(Queues.Graphics))
                    {
                        if (graphicsQueueFamilyIndex == -1) graphicsQueueFamilyIndex = i;

                        if (physicalDevice.GetSurfaceSupportKhr(i, Surface) &&
                            physicalDevice.GetWin32PresentationSupportKhr(i)) // TODO: x-plat
                        {
                            presentQueueFamilyIndex = i;
                            PhysicalDevice = physicalDevice;
                            break;
                        }
                    }
                }
                if (PhysicalDevice != null) break;
            }

            if (PhysicalDevice == null)
                throw new ApplicationException("No suitable physical device found.");

            // Create device.
            bool sameGraphicsAndPresent = graphicsQueueFamilyIndex == presentQueueFamilyIndex;
            var queueCreateInfos = new DeviceQueueCreateInfo[sameGraphicsAndPresent ? 1 : 2];
            queueCreateInfos[0] = new DeviceQueueCreateInfo(graphicsQueueFamilyIndex, new[] { 1.0f });
            if (!sameGraphicsAndPresent)
                queueCreateInfos[1] = new DeviceQueueCreateInfo(presentQueueFamilyIndex, new[] { 1.0f });

            var deviceCreateInfo = new DeviceCreateInfo(
                queueCreateInfos,
                new[] { Extensions.KhrSwapchain });
            Device = PhysicalDevice.CreateDevice(deviceCreateInfo);

            // Get queue.
            GraphicsQueue = Device.GetQueue(graphicsQueueFamilyIndex);
            PresentQueue = Device.GetQueue(presentQueueFamilyIndex);
        }

        private void CreateSwapchain()
        {
            SurfaceCapabilitiesKhr capabilities = PhysicalDevice.GetSurfaceCapabilitiesKhr(Surface);
            SurfaceFormatKhr[] formats = PhysicalDevice.GetSurfaceFormatsKhr(Surface);
            PresentModeKhr[] presentModes = PhysicalDevice.GetSurfacePresentModesKhr(Surface);
            Format format = formats.Length == 1 && formats[0].Format == Format.Undefined
                ? Format.B8G8R8A8UNorm
                : formats[0].Format;
            PresentModeKhr presentMode =
                presentModes.Contains(PresentModeKhr.Mailbox) ? PresentModeKhr.Mailbox :
                presentModes.Contains(PresentModeKhr.FifoRelaxed) ? PresentModeKhr.FifoRelaxed :
                presentModes.Contains(PresentModeKhr.Fifo) ? PresentModeKhr.Fifo :
                PresentModeKhr.Immediate;

            Swapchain = Device.CreateSwapchainKhr(new SwapchainCreateInfoKhr(
                Surface,
                capabilities.CurrentExtent,
                capabilities.CurrentTransform,
                presentMode,
                imageFormat: format));
            SwapchainImages = Swapchain.GetImages();
        }

        private void CreateSemaphoresAndCommandBuffers()
        {
            // Create semaphores.
            ImageAvailableSemaphore = Device.CreateSemaphore();
            RenderingFinishedSemaphore = Device.CreateSemaphore();

            // Create command buffers.
            CommandPool = Device.CreateCommandPool(new CommandPoolCreateInfo(GraphicsQueue.FamilyIndex));
            CommandBuffers = CommandPool.AllocateBuffers(new CommandBufferAllocateInfo(CommandBufferLevel.Primary, SwapchainImages.Length));
        }
    }
}
