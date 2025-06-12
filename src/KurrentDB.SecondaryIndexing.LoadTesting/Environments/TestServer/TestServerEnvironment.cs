// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.InMemory;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.TestServer;

public class TestServerEnvironment: ILoadTestEnvironment {
	private readonly SecondaryIndexingEnabledFixture _fixture;

	public IMessageBatchAppender MessageBatchAppender { get; }

	public TestServerEnvironment() {
		_fixture = new SecondaryIndexingEnabledFixture();
		MessageBatchAppender = new TestServerMessageBatchAppender(_fixture);
	}

	public async ValueTask InitAsync(CancellationToken ct = default) {
		await _fixture.InitializeAsync();
	}

	public async ValueTask DisposeAsync() {
		await MessageBatchAppender.DisposeAsync();
		await _fixture.DisposeAsync();
	}
}
