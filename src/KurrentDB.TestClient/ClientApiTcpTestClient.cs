// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.ClientAPI;
using ILogger = Serilog.ILogger;

namespace KurrentDB.TestClient;

/// <summary>
/// A test client that connects using the legacy dotnet TCP client
/// </summary>
public class ClientApiTcpTestClient {
	/// <summary>
	/// The options specified when starting the KurrentDB.TestClient
	/// </summary>
	public ClientOptions Options { get; set; }
	private ILogger _log;

	/// <summary>
	/// Constructs a new <see cref="ClientApiTcpTestClient"/>
	/// </summary>
	/// <param name="options"></param>
	/// <param name="log"></param>
	public ClientApiTcpTestClient(ClientOptions options, ILogger log) {
		Options = options;
		_log = log;
	}

	/// <summary>
	/// Creates a new TCP connection.
	/// </summary>
	/// <returns></returns>
	public IEventStoreConnection CreateConnection() {
		var connectionString = string.IsNullOrWhiteSpace(Options.ConnectionString)
			? $"ConnectTo=tcp://{Options.Host}:{Options.TcpPort};UseSslConnection={Options.UseTls};ValidateServer={Options.TlsValidateServer}"
			: Options.ConnectionString;
		_log.Debug("Creating TCP client with connection string '{connectionString}.", connectionString);

		var connectionSettings = ConnectionSettings.Create()
			.LimitRetriesForOperationTo(0)
			.KeepReconnecting();
		return EventStoreConnection.Create(connectionString, connectionSettings);
	}
}
