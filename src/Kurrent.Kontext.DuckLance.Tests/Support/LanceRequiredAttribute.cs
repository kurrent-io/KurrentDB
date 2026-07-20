using System.Runtime.InteropServices;

namespace DuckLance.Tests.Support;

/// <summary>
/// TUnit skip-gate that skips a test (or every test in a class) unless the current process is
/// running on a platform supported by DuckDB's <c>lance</c> extension.
/// </summary>
/// <remarks>
/// The <c>lance</c> extension only ships binaries for a subset of the platforms DuckDB itself
/// supports. As of this writing that set is: linux-x64, linux-arm64, osx-arm64, and win-x64.
/// Notably, osx-x64 (Intel Mac) is NOT supported. Apply this attribute to any test class or
/// method that needs to install/load the extension or otherwise depends on it being available.
/// </remarks>
public class LanceRequiredAttribute : SkipAttribute {
    const string UnsupportedPlatformReason = "DuckDB lance extension does not support this platform";

    public LanceRequiredAttribute()
        : base(UnsupportedPlatformReason) { }

    /// <inheritdoc />
    public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(!IsSupportedPlatform());

    /// <summary>
    /// Determines whether the current OS/architecture combination is one of the platforms the
    /// DuckDB <c>lance</c> extension ships binaries for: linux-x64, linux-arm64, osx-arm64, win-x64.
    /// </summary>
    static bool IsSupportedPlatform() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.OSArchitecture is Architecture.X64 or Architecture.Arm64;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.OSArchitecture is Architecture.Arm64;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.OSArchitecture is Architecture.X64;

        return false;
    }
}