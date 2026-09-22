// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Connectors;
using KurrentDB.Connectors.Planes.Control;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ConnectorsActivatorTests {
	const int Revision = 1;

	static (ConnectorsActivator Sut, ConnectorId ConnectorId) CreateSut(TestConnector connector) =>
		(new ConnectorsActivator((_, _) => connector), ConnectorId.From(Guid.NewGuid()));

	static ValueTask<ActivateResult> Activate(ConnectorsActivator sut, ConnectorId connectorId) =>
		sut.Activate(connectorId, NoSettings, Revision);

	static readonly Dictionary<string, string?> NoSettings = [];

	[Fact]
	public async Task connector_activates() {
		var connector = new TestConnector();
		var (sut, connectorId) = CreateSut(connector);

		var result = await Activate(sut, connectorId);

		result.Success.Should().BeTrue();
		result.Type.Should().Be(ActivateResultType.Activated);
		connector.DisposeCount.Should().Be(0);
		connector.ConnectionAttempt.Should().Be(1);
	}

	[Fact]
	public async Task connector_disposed_when_connect_throws_exception() {
		var exception = new InvalidOperationException("Connection failed");
		var connector = new TestConnector(failOnConnect: true, exception);
		var (sut, connectorId) = CreateSut(connector);

		var result = await Activate(sut, connectorId);

		result.Failure.Should().BeTrue();
		result.Type.Should().Be(ActivateResultType.Unknown);
		result.Error.Should().Be(exception);
		connector.DisposeCount.Should().Be(1);
		connector.ConnectionAttempt.Should().Be(1);
		connector.Stopped.Status.Should().Be(TaskStatus.RanToCompletion);
	}

	[Fact]
	public async Task connector_disposed_when_connect_throws_validation_exception() {
		var validationException = new FluentValidation.ValidationException("Invalid configuration");
		var connector = new TestConnector(failOnConnect: true, validationException);
		var (sut, connectorId) = CreateSut(connector);

		var result = await Activate(sut, connectorId);

		result.Failure.Should().BeTrue();
		result.Type.Should().Be(ActivateResultType.InvalidConfiguration);
		result.Error.Should().Be(validationException);
		connector.DisposeCount.Should().Be(1);
		connector.ConnectionAttempt.Should().Be(1);
	}

	[Fact]
	public async Task deactivates_once() {
		var connector = new TestConnector();
		var (sut, connectorId) = CreateSut(connector);

		await Activate(sut, connectorId);

		var result = await sut.Deactivate(connectorId);

		result.Type.Should().Be(DeactivateResultType.Deactivated);
		connector.DisposeCount.Should().Be(1);

		var repeated = await sut.Deactivate(connectorId);

		repeated.Type.Should().Be(DeactivateResultType.ConnectorNotFound);
		connector.DisposeCount.Should().Be(1);
	}

	[Fact]
	public async Task deactivates_self_stopped_connector() {
		var connector = new TestConnector();
		var (sut, connectorId) = CreateSut(connector);

		await Activate(sut, connectorId);

		// a sink failing against an unreachable broker
		connector.SimulateSelfTermination(new InvalidOperationException("simulated connector crash"));

		var result = await sut.Deactivate(connectorId);

		result.Type.Should().Be(DeactivateResultType.Deactivated);
		connector.DisposeCount.Should().Be(1);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task waits_for_deactivation(bool faulted) {
		var connector = new TestConnector();
		var (sut, connectorId) = CreateSut(connector);

		await Activate(sut, connectorId);

		var waiting = sut.WaitForDeactivation(connectorId);
		connector.SimulateSelfTermination(faulted ? new InvalidOperationException("simulated connector crash") : null);
		var result = await waiting;

		result.Type.Should().Be(DeactivateResultType.Deactivated);
		connector.DisposeCount.Should().Be(1);
	}
}

internal class TestConnector(bool failOnConnect = false, Exception? exception = null) : IConnector {
	readonly TaskCompletionSource _stoppedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public ConnectorId ConnectorId => ConnectorId.From(Guid.NewGuid());
	public ConnectorState State { get; private set; } = ConnectorState.Unspecified;
	public Task Stopped => _stoppedTcs.Task;

	public int DisposeCount      { get; private set; }
	public int ConnectionAttempt { get; private set; }

	public Task Connect(CancellationToken stoppingToken) {
		ConnectionAttempt++;

		if (failOnConnect) {
			State = ConnectorState.Stopped;
			throw exception ?? new InvalidOperationException("Connect failed");
		}

		State = ConnectorState.Running;
		return Task.CompletedTask;
	}

	public void SimulateSelfTermination(Exception? error = null) {
		State = ConnectorState.Stopped;

		if (error is null)
			_stoppedTcs.TrySetResult();
		else
			_stoppedTcs.TrySetException(error);
	}

	public ValueTask DisposeAsync() {
		DisposeCount++;
		State = ConnectorState.Stopped;

		_stoppedTcs.TrySetResult();
		return ValueTask.CompletedTask;
	}
}
