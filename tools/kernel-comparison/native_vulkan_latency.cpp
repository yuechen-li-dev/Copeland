// Direct C Vulkan API control for the managed submission/completion latency floor.
// No Aurelian, Silk.NET, shaders, timestamps, descriptors or validation layer.
#include <vulkan/vulkan.h>
#include <chrono>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

static void check(VkResult result)
{
    if (result != VK_SUCCESS)
    {
        throw std::runtime_error("Vulkan result " + std::to_string(result));
    }
}

struct Resources
{
    VkInstance instance{};
    VkDevice device{};
    VkCommandPool pool{};
    VkSemaphore timeline{};
    VkFence fence{};

    ~Resources()
    {
        if (device)
        {
            vkDeviceWaitIdle(device);
            if (fence) vkDestroyFence(device, fence, nullptr);
            if (timeline) vkDestroySemaphore(device, timeline, nullptr);
            if (pool) vkDestroyCommandPool(device, pool, nullptr);
            vkDestroyDevice(device, nullptr);
        }
        if (instance) vkDestroyInstance(instance, nullptr);
    }
};

using Clock = std::chrono::steady_clock;
static double microseconds(Clock::time_point first, Clock::time_point last)
{
    return std::chrono::duration<double, std::micro>(last - first).count();
}

int main()
{
    try
    {
        Resources resources;
        VkApplicationInfo application{VK_STRUCTURE_TYPE_APPLICATION_INFO};
        application.pApplicationName = "Aurelian native Vulkan latency control";
        application.apiVersion = VK_API_VERSION_1_3;
        VkInstanceCreateInfo instanceInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
        instanceInfo.pApplicationInfo = &application;
        check(vkCreateInstance(&instanceInfo, nullptr, &resources.instance));
        uint32_t count = 0;
        check(vkEnumeratePhysicalDevices(resources.instance, &count, nullptr));
        std::vector<VkPhysicalDevice> devices(count);
        check(vkEnumeratePhysicalDevices(resources.instance, &count, devices.data()));
        VkPhysicalDevice physical{};
        VkPhysicalDeviceProperties properties{};
        for (auto candidate : devices)
        {
            vkGetPhysicalDeviceProperties(candidate, &properties);
            if (properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU)
            {
                physical = candidate;
                break;
            }
        }
        if (!physical) throw std::runtime_error("No discrete GPU found");
        vkGetPhysicalDeviceQueueFamilyProperties(physical, &count, nullptr);
        std::vector<VkQueueFamilyProperties> families(count);
        vkGetPhysicalDeviceQueueFamilyProperties(physical, &count, families.data());
        uint32_t family = 0;
        for (; family < count; ++family)
        {
            auto required = VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT;
            if ((families[family].queueFlags & required) == required) break;
        }
        if (family == count) throw std::runtime_error("No graphics/compute queue family");
        float priority = 1.0f;
        VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
        queueInfo.queueFamilyIndex = family;
        queueInfo.queueCount = 1;
        queueInfo.pQueuePriorities = &priority;
        VkPhysicalDeviceTimelineSemaphoreFeatures timelineFeature{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES};
        timelineFeature.timelineSemaphore = VK_TRUE;
        VkDeviceCreateInfo deviceInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
        deviceInfo.pNext = &timelineFeature;
        deviceInfo.queueCreateInfoCount = 1;
        deviceInfo.pQueueCreateInfos = &queueInfo;
        check(vkCreateDevice(physical, &deviceInfo, nullptr, &resources.device));
        VkQueue queue{};
        vkGetDeviceQueue(resources.device, family, 0, &queue);
        VkCommandPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
        poolInfo.queueFamilyIndex = family;
        check(vkCreateCommandPool(resources.device, &poolInfo, nullptr, &resources.pool));
        VkCommandBufferAllocateInfo allocation{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
        allocation.commandPool = resources.pool;
        allocation.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        allocation.commandBufferCount = 1;
        VkCommandBuffer command{};
        check(vkAllocateCommandBuffers(resources.device, &allocation, &command));
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
        check(vkBeginCommandBuffer(command, &begin));
        check(vkEndCommandBuffer(command));
        VkSemaphoreTypeCreateInfo semaphoreType{VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO};
        semaphoreType.semaphoreType = VK_SEMAPHORE_TYPE_TIMELINE;
        VkSemaphoreCreateInfo semaphoreInfo{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};
        semaphoreInfo.pNext = &semaphoreType;
        check(vkCreateSemaphore(resources.device, &semaphoreInfo, nullptr, &resources.timeline));
        VkFenceCreateInfo fenceInfo{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
        check(vkCreateFence(resources.device, &fenceInfo, nullptr, &resources.fence));

        struct Sample { std::string mode; int sample; double wall, submit, wait; };
        std::vector<Sample> samples;
        samples.reserve(1500);
        uint64_t value = 0;
        for (int sample = -50; sample < 500; ++sample)
        {
            for (const std::string mode : {"timeline", "fence", "idle"})
            {
                auto start = Clock::now();
                VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};
                submit.commandBufferCount = 1;
                submit.pCommandBuffers = &command;
                VkTimelineSemaphoreSubmitInfo timeline{VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO};
                if (mode == "timeline")
                {
                    ++value;
                    timeline.signalSemaphoreValueCount = 1;
                    timeline.pSignalSemaphoreValues = &value;
                    submit.pNext = &timeline;
                    submit.signalSemaphoreCount = 1;
                    submit.pSignalSemaphores = &resources.timeline;
                }
                auto prepared = Clock::now();
                check(vkQueueSubmit(queue, 1, &submit, mode == "fence" ? resources.fence : VK_NULL_HANDLE));
                auto queued = Clock::now();
                if (mode == "timeline")
                {
                    VkSemaphoreWaitInfo wait{VK_STRUCTURE_TYPE_SEMAPHORE_WAIT_INFO};
                    wait.semaphoreCount = 1;
                    wait.pSemaphores = &resources.timeline;
                    wait.pValues = &value;
                    check(vkWaitSemaphores(resources.device, &wait, 5'000'000'000ULL));
                }
                else if (mode == "fence")
                {
                    check(vkWaitForFences(resources.device, 1, &resources.fence, VK_TRUE, 5'000'000'000ULL));
                }
                else
                {
                    check(vkQueueWaitIdle(queue));
                }
                auto completed = Clock::now();
                if (mode == "fence") check(vkResetFences(resources.device, 1, &resources.fence));
                auto finished = Clock::now();
                if (sample >= 0)
                {
                    samples.push_back({mode, sample, microseconds(start, finished),
                        microseconds(prepared, queued), microseconds(queued, completed)});
                }
            }
        }
        std::cout << "# device=" << properties.deviceName << ",queue_family=" << family << '\n';
        std::cout << "Mode,Sample,WallMicroseconds,QueueSubmitMicroseconds,CompletionWaitMicroseconds\n";
        for (const auto& sample : samples)
        {
            std::cout << sample.mode << ',' << sample.sample << ',' << sample.wall << ','
                << sample.submit << ',' << sample.wait << '\n';
        }
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
