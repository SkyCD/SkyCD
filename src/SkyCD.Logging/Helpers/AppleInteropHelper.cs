using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SkyCD.Logging.Helpers;

[SupportedOSPlatform(SupportedOsPlatforms.MacOs)]
[SupportedOSPlatform(SupportedOsPlatforms.Ios)]
[SupportedOSPlatform(SupportedOsPlatforms.TvOs)]
[SupportedOSPlatform(SupportedOsPlatforms.WatchOs)]
internal static class AppleInteropHelper
{
    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "os_log_create")]
    public static extern IntPtr CreateAppleLogHandle(string subsystem, string category);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "os_log_with_type", CallingConvention = CallingConvention.Cdecl)]
    public static extern void WriteAppleLogMessage(IntPtr osLog, byte type, string format, __arglist);
}
