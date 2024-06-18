using System.Diagnostics;
using System.Runtime.InteropServices;

namespace McMaster.NETCore.Plugins;

static class PlatformInformation {
    public static readonly string[] ManagedAssemblyExtensions = new[] {
        ".dll",
        ".ni.dll",
        ".exe",
        ".ni.exe"
    };

    public static readonly string[] NativeLibraryExtensions;
    public static readonly string[] NativeLibraryPrefixes;

    static PlatformInformation() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            NativeLibraryPrefixes   = new[] { "" };
            NativeLibraryExtensions = new[] { ".dll" };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            NativeLibraryPrefixes   = new[] { "", "lib" };
            NativeLibraryExtensions = new[] { ".dylib" };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            NativeLibraryPrefixes   = new[] { "", "lib" };
            NativeLibraryExtensions = new[] { ".so", ".so.1" };
        }
        else {
            Debug.Fail("Unknown OS type");
            NativeLibraryPrefixes   = Array.Empty<string>();
            NativeLibraryExtensions = Array.Empty<string>();
        }
    }
}