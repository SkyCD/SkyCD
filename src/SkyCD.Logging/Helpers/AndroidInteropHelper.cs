using System.Runtime.Versioning;
#if ANDROID
using Android.Util;
#endif

namespace SkyCD.Logging.Helpers;

[SupportedOSPlatform(SupportedOsPlatforms.Android)]
internal static class AndroidInteropHelper
{
    public static void WriteLogLine(int priority, string tag, string message)
    {
#if ANDROID
        Log.WriteLine((LogPriority)priority, tag, message);
#endif
    }
}
