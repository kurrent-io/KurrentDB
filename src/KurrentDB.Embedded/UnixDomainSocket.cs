// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net.Http;
using System.Net.Sockets;

namespace KurrentDB.Embedded;

/// <summary>
/// The client side of the embedded server's UNIX domain socket, and the file-permission handling that
/// keeps it to the user who started the database.
/// </summary>
static class UnixDomainSocket {
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

	/// <summary>
	/// Creates <paramref name="path"/> if it does not exist, readable only by the current user when the
	/// directory is ours to create. Returns the full path.
	/// </summary>
	public static string CreatePrivateDirectory(string path) {
		var fullPath = Path.GetFullPath(path);

		if (Directory.Exists(fullPath))
			return fullPath;

		if (OperatingSystem.IsWindows())
			Directory.CreateDirectory(fullPath);
		else
			Directory.CreateDirectory(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		return fullPath;
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
