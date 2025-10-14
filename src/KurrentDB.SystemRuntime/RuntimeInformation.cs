// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using System.Reflection;
using System.Runtime.InteropServices;
using static System.Environment;
using static System.Runtime.InteropServices.RuntimeInformation;
using static System.String;

namespace System.Runtime;

[PublicAPI]
public static class RuntimeInformation {
	static RuntimeInformation() {
		if (IsOSPlatform(OSPlatform.OSX)) {
			OsPlatform = RuntimeOSPlatform.OSX;
			IsOSX = true;
		} else if (IsOSPlatform(OSPlatform.Linux)) {
			OsPlatform = RuntimeOSPlatform.Linux;
			IsLinux = true;
		} else if (IsOSPlatform(OSPlatform.Windows)) {
			OsPlatform = RuntimeOSPlatform.Windows;
			IsWindows = true;
		} else if (IsOSPlatform(OSPlatform.FreeBSD)) {
			OsPlatform = RuntimeOSPlatform.FreeBSD;
			IsFreeBSD = true;
		} else
			OsPlatform = RuntimeOSPlatform.Unknown;

		IsUnix = IsLinux || IsFreeBSD || IsOSX;

		IsRunningInContainer = !IsNullOrEmpty(GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"));
		IsRunningInKubernetes = !IsNullOrEmpty(GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));

		Host = DotNetHostInfo.Collect();
		RuntimeVersion = Host.RuntimeVersion;
		RuntimeMode = Host.Mode;

		HomeFolder = GetFolderPath(SpecialFolder.UserProfile);
	}

	/// <summary>
	/// Indicates if the current process is running in a container.
	/// </summary>
	public static readonly bool IsRunningInContainer;

	/// <summary>
	/// Indicates if the current process is running in a Kubernetes cluster.
	/// </summary>
	public static readonly bool IsRunningInKubernetes;

	/// <summary>
	/// The operating system platform the current process is running on.
	/// </summary>
	public static readonly RuntimeOSPlatform OsPlatform;

	/// <summary>
	/// Indicates if the current operating system is Linux.
	/// </summary>
	public static readonly bool IsLinux;

	/// <summary>
	/// Indicates if the current operating system is Windows.
	/// </summary>
	public static readonly bool IsWindows;

	/// <summary>
	/// Indicates if the current operating system is macOS.
	/// </summary>
	public static readonly bool IsOSX;

	/// <summary>
	/// Indicates if the current operating system is FreeBSD.
	/// </summary>
	public static readonly bool IsFreeBSD;

	/// <summary>
	/// Indicates if the current operating system is a Unix-based system (Linux, FreeBSD or macOS).
	/// </summary>
	public static readonly bool IsUnix;

	/// <summary>
	/// Information about the .NET host environment.
	/// This includes details such as the version of the .NET runtime, the architecture, the mode (e.g., 64 for a 64-bit runtime), the commit hash of the .NET runtime, and a custom runtime version of the .NET host.
	/// </summary>
	public static readonly DotNetHostInfo Host;

	/// <summary>
	/// The user's profile folder.
	/// </summary>
	/// <remarks>
	/// Applications should not create files or folders at this level; they should put their data under the locations referred to by <see cref="F:System.Environment.SpecialFolder.ApplicationData" />.
	/// </remarks>
	public static readonly string HomeFolder;

	/// <summary>
	/// Custom runtime version of the .NET host.
	/// </summary>
	public static readonly string RuntimeVersion;

	/// <summary>
	/// The mode of the .NET runtime, represented as the size of a pointer (e.g., 64 for a 64-bit runtime).
	/// </summary>
	public static readonly int RuntimeMode;
}

/// <summary>
/// Represents information about the .NET host environment.
/// </summary>
/// <param name="Version">The version of the .NET runtime.</param>
/// <param name="Architecture">The architecture of the .NET runtime (e.g., x64, arm64).</param>
/// <param name="Mode">The mode of the .NET runtime, represented as the size of a pointer (e.g., 64 for a 64-bit runtime).</param>
/// <param name="Commit">The commit hash of the .NET runtime.</param>
/// <param name="RuntimeVersion">Custom runtime version of the .NET host.</param>
public readonly record struct DotNetHostInfo(string Version, Architecture Architecture, int Mode, string Commit, string RuntimeVersion) {
	public override string ToString() => RuntimeVersion;

	public static DotNetHostInfo Collect() {
		var assemblyVersion = typeof(object).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion!;

		var commit = assemblyVersion.Substring(assemblyVersion.IndexOf('+') + 1, 9);

		return new() {
			Version = Environment.Version.ToString(),
			Architecture = OSArchitecture,
			Mode = IntPtr.Size * 8,
			Commit = commit,
			RuntimeVersion = $"{FrameworkDescription}/{commit}"
		};
	}
}
