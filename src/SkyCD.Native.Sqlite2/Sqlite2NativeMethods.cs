using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SkyCD.Native.Sqlite2;

internal static class Sqlite2NativeMethods
{
    private const string Library = "skycd_sqlite2";
    internal static string Runtime => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    static Sqlite2NativeMethods() => NativeLibrary.SetDllImportResolver(typeof(Sqlite2NativeMethods).Assembly, Resolve);

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return IntPtr.Zero;
        var file = OperatingSystem.IsWindows() ? "skycd_sqlite2.dll" : OperatingSystem.IsMacOS() ? "libskycd_sqlite2.dylib" : "libskycd_sqlite2.so";
        var path = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "runtimes", Runtime, "native", file);
        if (!File.Exists(path)) throw new Sqlite2RuntimeUnavailableException(Runtime);
        try { return NativeLibrary.Load(path); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        { throw new Sqlite2RuntimeUnavailableException(Runtime); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int Authorizer(IntPtr user, int action, IntPtr first, IntPtr second, IntPtr database, IntPtr trigger);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int Progress(IntPtr user);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_libversion")]
    internal static extern IntPtr Version();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_open")]
    internal static extern IntPtr Open([MarshalAs(UnmanagedType.LPStr)] string path, int mode, out IntPtr error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_close")]
    internal static extern void Close(IntPtr database);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_compile")]
    internal static extern int Compile(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, out IntPtr tail, out IntPtr statement, out IntPtr error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_step")]
    internal static extern int Step(IntPtr statement, out int columns, out IntPtr values, out IntPtr names);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_finalize")]
    internal static extern int FinalizeStatement(IntPtr statement, out IntPtr error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_freemem")]
    internal static extern void Free(IntPtr error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_set_authorizer")]
    internal static extern int SetAuthorizer(IntPtr database, Authorizer authorizer, IntPtr user);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite_progress_handler")]
    internal static extern void SetProgress(IntPtr database, int operations, Progress progress, IntPtr user);
}
