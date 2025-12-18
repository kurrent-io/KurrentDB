// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.DuckDB;
using KurrentDB.Core.Services.Transport.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace KurrentDB;

public static class KestrelHelpers {
	public static void ConfigureHttpOptions(ListenOptions listenOptions, ClusterVNodeHostedService hostedService, bool useHttps) {
		listenOptions.UseDuckDbConnectionPoolPerConnection();

		if (useHttps)
			listenOptions.UseHttps(CreateServerOptionsSelectionCallback(hostedService), null);
		else
			listenOptions.Use(next => new ClearTextHttpMultiplexingMiddleware(next).OnConnectAsync);
	}

	// Attaches a duck connection pool (duck pond??) to the kestrel connection because issuing secondary index reads will
	// build query plans and cache them on the (pooled) duckdb connection and we dont want to end up with too many of these over time.
	// If a pool is not attached to the connection the reading infra will use the shared pool.
	public static void UseDuckDbConnectionPoolPerConnection(this ListenOptions listenOptions) {
		listenOptions.Use(next => async connectionContext => {
			var poolFactory = listenOptions.ApplicationServices.GetRequiredService<DuckDBConnectionPoolLifetime>();
			using var pool = poolFactory.CreatePool();
			connectionContext.Items[nameof(DuckDBConnectionPool)] = pool;
			await next(connectionContext);
		});
	}

	public static void TryListenOnUnixSocket(ClusterVNodeHostedService hostedService, KestrelServerOptions server) {
		if (hostedService.Node.Db.Config.InMemDb) {
			Log.Information("Not listening on a UNIX domain socket since the database is running in memory.");
			return;
		}

		if (!RuntimeInformation.IsLinux && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17063)) {
			Log.Error("Not listening on a UNIX domain socket since it is not supported by the operating system.");
			return;
		}

		try {
			var legacyUnixSocket = Path.GetFullPath(Path.Combine(hostedService.Node.Db.Config.Path, "eventstore.sock"));
			var unixSocket = Path.GetFullPath(Path.Combine(hostedService.Node.Db.Config.Path, "kurrent.sock"));

			CleanupStaleSocket(legacyUnixSocket);
			CleanupStaleSocket(unixSocket);

			server.ListenUnixSocket(unixSocket, listenOptions => {
				listenOptions.Use(next => new UnixSocketConnectionMiddleware(next).OnConnectAsync);
				ConfigureHttpOptions(listenOptions, hostedService, useHttps: false);
			});
			Log.Information("Listening on UNIX domain socket: {unixSocket}", unixSocket);
		} catch (Exception ex) {
			Log.Error(ex, "Failed to listen on UNIX domain socket.");
			throw;
		}

		return;

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
				RemoteCertificateValidationCallback = (_, certificate, chain, sslPolicyErrors) => {
					if (certificate == null) // not necessary to have a client certificate
						return true;

					var (isValid, error) =
						hostedService.Node.InternalClientCertificateValidator(
							certificate,
							chain,
							sslPolicyErrors);
					if (!isValid && error != null) {
						Log.Error("Client certificate validation error: {e}", error);
					}

					return isValid;
				},
				CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
				EnabledSslProtocols = SslProtocols.None, // let the OS choose a secure TLS protocol
				ApplicationProtocols = [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11],
				AllowRenegotiation = false
			};

			return ValueTask.FromResult(serverOptions);
		};
	}
}
