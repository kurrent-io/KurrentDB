// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.gRPC;

public class gRPCClientEnvironment : ILoadTestEnvironment {
	public IMessageBatchAppender MessageBatchAppender { get; }
	public IIndexingSummaryAssertion AssertThat { get; }
	private readonly KurrentDBClient _client;

	public gRPCClientEnvironment(string dbConnectionString) {
		_client = new KurrentDBClient(KurrentDBClientSettings.Create(dbConnectionString));
		MessageBatchAppender = new gRPCMessageBatchAppender(_client);
		AssertThat = new DummyIndexingSummaryAssertion();
	}

	public ValueTask InitializeAsync(CancellationToken ct = default) =>
		ValueTask.CompletedTask;

	public async ValueTask DisposeAsync() {
		await MessageBatchAppender.DisposeAsync();
		await _client.DisposeAsync();
	}
}
