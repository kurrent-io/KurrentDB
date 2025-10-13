// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Kurrent.Surge.Consumers.Configuration;
using Kurrent.Surge.DuckDB;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.SchemaRegistry.Planes.Projection;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MemberInfo = KurrentDB.Core.Cluster.MemberInfo;

namespace KurrentDB.SchemaRegistry.Tests.Planes.Projection;

public class DuckDBProjectorServiceTests : SchemaApplicationTestFixture {
	static readonly MessageBus MessageBus = new();
	static readonly MemberInfo FakeMemberInfo = MemberInfo.ForManager(Guid.NewGuid(), DateTime.Now, true, new IPEndPoint(0, 0));

	[Test]
	public async Task duckdb_projector_service_runs_on_all_nodes(CancellationToken cancellationToken) {
		// Arrange
		var sut = new TestDuckDBProjectorService(
			MessageBus,
			MessageBus,
			Fixture.DuckDbConnectionProvider,
			Fixture.Services.GetRequiredService<IConsumerBuilder>(),
			Fixture.LoggerFactory
		);

		await sut.StartAsync(cancellationToken);

		// Act
		MessageBus.Publish(new SystemMessage.BecomeLeader(Guid.NewGuid()));

		var executing = await sut.WaitUntilExecuting();

		// Assert
		executing.IsCancellationRequested.ShouldBeFalse();

		// Act
		MessageBus.Publish(new SystemMessage.BecomeFollower(Guid.NewGuid(), FakeMemberInfo));

		await Task.Delay(1000, cancellationToken);

		// Assert
		executing.IsCancellationRequested.ShouldBeFalse();
	}
}

class TestDuckDBProjectorService(
	IPublisher publisher,
	ISubscriber subscriber,
	IDuckDBConnectionProvider connectionProvider,
	IConsumerBuilder consumerBuilder,
	ILoggerFactory loggerFactory
) : DuckDBProjectorService(publisher, subscriber, connectionProvider, consumerBuilder, loggerFactory) {
	volatile TaskCompletionSource<CancellationToken> _executingCompletionSource = new();
	volatile TaskCompletionSource<CancellationToken> _executedCompletionSource = new();

	public TimeSpan ExecuteDelay { get; set; } = TimeSpan.FromMinutes(10);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		_executingCompletionSource.SetResult(stoppingToken);
		await Tasks.SafeDelay(ExecuteDelay, stoppingToken);
		_executedCompletionSource.SetResult(stoppingToken);
	}

	public async Task<CancellationToken> WaitUntilExecuting() {
		var result = await _executingCompletionSource.Task;
		_executingCompletionSource = new();
		return result;
	}

	public async Task<CancellationToken> WaitUntilExecuted() {
		var result = await _executedCompletionSource.Task;
		_executedCompletionSource = new();
		return result;
	}
}
