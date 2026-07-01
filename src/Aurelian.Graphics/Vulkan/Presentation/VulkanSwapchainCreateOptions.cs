namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed record VulkanSwapchainCreateOptions(
    uint Width = 640,
    uint Height = 480,
    bool VSync = true,
    string Title = "Aurelian",
    bool Visible = false);
