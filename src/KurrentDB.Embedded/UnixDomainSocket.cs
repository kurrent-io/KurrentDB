// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace KurrentDB.Embedded;

/// <summary>
/// The client side of the embedded server's UNIX domain socket, and the file-permission handling that
/// keeps it to the user who started the database.
/// </summary>
public static class UnixDomainSocket {
	/// <summary>
	/// Creates a handler that dials the socket at <paramref name="socketPath"/> instead of resolving and
	/// connecting to the address it is given. The address then only supplies the scheme and the
	/// <c>:authority</c> header.
	/// </summary>
	public static SocketsHttpHandler CreateHandler(string socketPath) =>
		new() {
			ConnectCallback = async (_, cancellationToken) => {
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				try {
					await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
					return new NetworkStream(socket, ownsSocket: true);
				} catch {
					socket.Dispose();
					throw;
				}
			},
			// a catch-up subscription should not have to share one connection's concurrent stream limit with
			// the appends the same process is making
			EnableMultipleHttp2Connections = true
			// no keep-alive pings: there is no network path to keep open, and nothing in between to time the
			// connection out
		};

	const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

	// A socket address carries its path in sockaddr_un.sun_path, a fixed 108 bytes on Linux and Windows
	// and 104 on macOS, and the path is stored there with a terminating NUL. So a socket path can be one
	// byte less than the array, whatever the "between 1 and 108 characters" in .NET's own message for this
	// suggests: it is counting the terminator.
	static int MaxSocketPathBytes => OperatingSystem.IsMacOS() ? 103 : 107;

	/// <summary>
	/// Creates <paramref name="path"/> if it does not exist, and leaves it reachable only by the current
	/// user whether it was created here or found. Returns the full path.
	/// </summary>
	/// <remarks>
	/// The socket is bound inside this directory, and the node binds it before anything can narrow it: it
	/// is created with whatever the process umask allows, and only restricted once the server is already
	/// accepting connections. A directory no one else can traverse is what closes that window, so a
	/// directory that already exists is narrowed rather than taken as it is found.
	/// </remarks>
	/// <exception cref="UnauthorizedAccessException">
	/// The directory is reachable by other users and is not ours to narrow.
	/// </exception>
	public static string CreatePrivateDirectory(string path) {
		var fullPath = Path.GetFullPath(path);

		EnsureTheSocketPathFits(fullPath, nameof(path));

		var existed = Directory.Exists(fullPath);

		if (OperatingSystem.IsWindows()) {
			Directory.CreateDirectory(fullPath);
			return fullPath;
		}

		if (!existed) {
			Directory.CreateDirectory(fullPath, OwnerOnly);
			return fullPath;
		}

		var mode = File.GetUnixFileMode(fullPath);

		if ((mode & ~OwnerOnly) == 0)
			return fullPath;

		try {
			File.SetUnixFileMode(fullPath, OwnerOnly);
		} catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
			throw new UnauthorizedAccessException(
				$"The directory '{fullPath}' can be reached by other users and could not be restricted to this " +
				"one. An embedded database is reached over a UNIX domain socket inside its data directory, and a " +
				"connection over that socket is authenticated as the system account.", ex);
		}

		return fullPath;
	}

	/// <summary>
	/// Refuses a directory the node could not bind its socket in. The limit is on the socket address, not
	/// on the path, so nothing about the directory gives this away: it is the node that fails, once the
	/// database is already open and the listener is being set up.
	/// </summary>
	static void EnsureTheSocketPathFits(string fullPath, string paramName) {
		var socketPath = Path.Combine(fullPath, KestrelHelpers.UnixSocketFileName);
		var length = Encoding.UTF8.GetByteCount(socketPath);

		if (length <= MaxSocketPathBytes)
			return;

		throw new ArgumentException(
			$"The data directory '{fullPath}' is too deeply nested for an embedded database. It is reached " +
			$"over a UNIX domain socket at '{socketPath}', and a socket path on this platform is limited to " +
			$"{MaxSocketPathBytes} bytes rather than the {length} that needs. Use a shorter data directory.",
			paramName);
	}

	/// <summary>
	/// Restricts the bound socket to its owner. A connection over the socket is authenticated as the system
	/// account, so the file permissions are what stands between another local user and an administrator's
	/// view of the database.
	/// </summary>
	public static void RestrictToOwner(string socketPath) {
		if (OperatingSystem.IsWindows())
			return;

		File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
	}

	public static void Delete(string socketPath) {
		try {
			File.Delete(socketPath);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// the next start cleans up a socket file left behind
		}
	}
}
