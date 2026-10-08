// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net.Security;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Services.Transport.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace KurrentDB;

public static class KestrelHelpers {
	/// <returns>True if a UNIX domain socket was opened, with <paramref name="unixSocket"/> naming it.</returns>
	public static bool TryConfigureListeners(
		KestrelServerOptions server,
		ClusterVNodeOptions options,
		ClusterVNodeHostedService hostedService,
		bool listenOnTcp,
		out string unixSocket) {

		server.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromMilliseconds(options.Grpc.KeepAliveInterval);
		server.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromMilliseconds(options.Grpc.KeepAliveTimeout);

		if (listenOnTcp) {
			server.Listen(options.Interface.NodeIp, options.Interface.NodePort, listenOptions =>
				ConfigureHttpOptions(listenOptions, hostedService, useHttps: !hostedService.Node.DisableHttps));
		}

		unixSocket = null;
		return hostedService.Node.EnableUnixSocket && TryListenOnUnixSocket(hostedService, server, out unixSocket);
	}

	public static void ConfigureHttpOptions(ListenOptions listenOptions, ClusterVNodeHostedService hostedService, bool useHttps) {
		listenOptions.UseConnectionInterceptors();

		if (useHttps)
			listenOptions.UseHttps(CreateServerOptionsSelectionCallback(hostedService), null);
		else
			listenOptions.Use(next => new ClearTextHttpMultiplexingMiddleware(next).OnConnectAsync);
	}

	public static bool TryListenOnUnixSocket(ClusterVNodeHostedService hostedService, KestrelServerOptions server, out string unixSocket) {
		unixSocket = null;

		if (hostedService.Node.Db.Config.InMemDb) {
			Log.Information("Not listening on a UNIX domain socket since the database is running in memory.");
			return false;
		}

		if (!RuntimeInformation.IsLinux && !RuntimeInformation.IsOSX && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17063)) {
			Log.Error("Not listening on a UNIX domain socket since it is not supported by the operating system.");
			return false;
		}

		try {
			var legacyUnixSocket = Path.GetFullPath(Path.Combine(hostedService.Node.Db.Config.Path, "eventstore.sock"));
			unixSocket = Path.GetFullPath(Path.Combine(hostedService.Node.Db.Config.Path, "kurrent.sock"));

			CleanupStaleSocket(legacyUnixSocket);
			CleanupStaleSocket(unixSocket);

			server.ListenUnixSocket(unixSocket, listenOptions => {
				listenOptions.Use(next => new UnixSocketConnectionMiddleware(next).OnConnectAsync);
				ConfigureHttpOptions(listenOptions, hostedService, useHttps: false);
			});
			Log.Information("Listening on UNIX domain socket: {unixSocket}", unixSocket);

			return true;
		} catch (Exception ex) {
			Log.Error(ex, "Failed to listen on UNIX domain socket.");
			throw;
		}

		static void CleanupStaleSocket(string socketPath) {
			if (File.Exists(socketPath)) {
				try {
					File.Delete(socketPath);
					Log.Information("Cleaned up stale UNIX domain socket: {unixSocket}", socketPath);
				} catch (Exception ex) {
					Log.Error(ex, "Failed to clean up stale UNIX domain socket: {unixSocket}. Please delete the file manually.", socketPath);
					throw;
				}
			}
		}
	}

	public static ServerOptionsSelectionCallback CreateServerOptionsSelectionCallback(ClusterVNodeHostedService hostedService) {
		return (_, _, _, _) => {
			var serverOptions = new SslServerAuthenticationOptions {
				ServerCertificateContext = SslStreamCertificateContext.Create(
					hostedService.Node.CertificateSelector(),
					hostedService.Node.IntermediateCertificatesSelector(),
					offline: true),
				ClientCertificateRequired = true, // request a client certificate but it's not necessary for the client to supply one
				RemoteCertificateValidationCallback = NodeTlsPolicy.ForClientCertificate((certificate, chain, sslPolicyErrors) => {
					if (certificate is null)
						return (true, null);

					return hostedService.Node.InternalClientCertificateValidator(
						certificate,
						chain,
						sslPolicyErrors);
				}),
				CertificateRevocationCheckMode = NodeTlsPolicy.CertificateRevocationCheckMode,
				EnabledSslProtocols = NodeTlsPolicy.SystemSslProtocols,
				ApplicationProtocols = [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11],
				AllowRenegotiation = NodeTlsPolicy.AllowRenegotiation
			};

			return ValueTask.FromResult(serverOptions);
		};
	}
}
