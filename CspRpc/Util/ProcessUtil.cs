using System.Diagnostics;
using System.Management;

namespace CspRpc.Util;

internal static class ProcessUtil
{
    internal static string? GetCommandLine(this Process process)
    {
        using (var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
        using (var objects = searcher.Get())
        using (var obj = Enumerable.Cast<ManagementBaseObject>(objects).SingleOrDefault())
        {
           return obj?["CommandLine"]?.ToString().Trim();
        }
    }
}
