namespace ShadeOfColor2.Core.Services;

public static class MemoryMonitor
{
    private static readonly long MaxMemoryBytes = 200 * 1024 * 1024; // 200MB threshold
    private static readonly long CriticalMemoryBytes = 400 * 1024 * 1024; // 400MB critical
    
    public static bool IsMemoryPressureHigh()
    {
        var memoryUsage = GC.GetTotalMemory(false);
        return memoryUsage > MaxMemoryBytes;
    }
    
    public static bool IsMemoryCritical()
    {
        var memoryUsage = GC.GetTotalMemory(false);
        return memoryUsage > CriticalMemoryBytes;
    }
    
    public static void ForceCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
    
    public static long GetCurrentMemoryUsage()
    {
        return GC.GetTotalMemory(false);
    }
    
    public static void ThrowIfMemoryCritical()
    {
        if (IsMemoryCritical())
        {
            ForceCleanup();
            if (IsMemoryCritical())
            {
                throw new InvalidOperationException("Server memory critically low. Please try again later.");
            }
        }
    }
}