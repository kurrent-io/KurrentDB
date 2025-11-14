// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Connectors;
using KurrentDB.Connectors.Planes.Control;

namespace KurrentDB.Connectors.Tests.Planes.Control;

[Trait("Category", "ControlPlane")]
public class ConnectorsActivatorTests {
	[Fact]
	public async Task connector_activates() {
		// Arrange
		var connectorId = ConnectorId.From(Guid.NewGuid());
		var settings = new Dictionary<string, string?>();
		var revision = 1;

		var testConnector = new TestConnector(failOnConnect: false);

		var sut = new ConnectorsActivator(CreateConnector);

		// Act
		var result = await sut.Activate(connectorId, settings, revision);

		// Assert
		result.Success.Should().BeTrue();
		result.Type.Should().Be(ActivateResultType.Activated);
		testConnector.IsDisposed.Should().BeFalse();
		testConnector.ConnectionAttempt.Should().Be(1);
		return;

		IConnector CreateConnector(ConnectorId connectorId1, IDictionary<string, string?> dictionary) => testConnector;
	}

	[Fact]
	public async Task connector_disposed_when_connect_throws_exception() {
		// Arrange
		var connectorId = ConnectorId.From(Guid.NewGuid());
		var settings = new Dictionary<string, string?>();
		var revision = 1;
		var exception = new InvalidOperationException("Connection failed");

		var testConnector = new TestConnector(failOnConnect: true, exception);

		var sut = new ConnectorsActivator(CreateConnector);

		// Act
		var result = await sut.Activate(connectorId, settings, revision);

		// Assert
		result.Failure.Should().BeTrue();
		result.Type.Should().Be(ActivateResultType.Unknown);
		result.Error.Should().Be(exception);
		testConnector.IsDisposed.Should().BeTrue();
		testConnector.ConnectionAttempt.Should().Be(1);
		return;

		IConnector CreateConnector(ConnectorId connectorId1, IDictionary<string, string?> dictionary) => testConnector;
	}

	[Fact]
	public async Task connector_disposed_when_connect_throws_validation_exception() {
		// Arrange
		var connectorId = ConnectorId.From(Guid.NewGuid());
		var settings = new Dictionary<string, string?>();
		var revision = 1;
		var validationException = new FluentValidation.ValidationException("Invalid configuration");

		var testConnector = new TestConnector(failOnConnect: true, validationException);

		var sut = new ConnectorsActivator(CreateConnector);

		// Act
		var result = await sut.Activate(connectorId, settings, revision);

		// Assert
		result.Failure.Should().BeTrue();
		result.Type.Should().Be(ActivateResultType.InvalidConfiguration);
		result.Error.Should().Be(validationException);
		testConnector.IsDisposed.Should().BeTrue();
		testConnector.ConnectionAttempt.Should().Be(1);
		return;

		IConnector CreateConnector(ConnectorId connectorId1, IDictionary<string, string?> dictionary) => testConnector;
	}

	[Fact]
	public async Task connector_stopped_task_completes_on_connect_failure() {
		// Arrange
		var connectorId = ConnectorId.From(Guid.NewGuid());
		var settings = new Dictionary<string, string?>();
		var revision = 1;
		var exception = new InvalidOperationException("Connection failed");

		var testConnector = new TestConnector(failOnConnect: true, exception);

		var sut = new ConnectorsActivator(CreateConnector);

		// Act
		var result = await sut.Activate(connectorId, settings, revision);

		// Assert
		result.Failure.Should().BeTrue();
		testConnector.Stopped.IsCompleted.Should().BeTrue();
		testConnector.Stopped.Status.Should().Be(TaskStatus.RanToCompletion);
		return;

		IConnector CreateConnector(ConnectorId connectorId1, IDictionary<string, string?> dictionary) => testConnector;
	}
}

internal class TestConnector(bool failOnConnect = false, Exception? exception = null) : IConnector {
	readonly TaskCompletionSource _stoppedTcs = new();

	public ConnectorId ConnectorId => ConnectorId.From(Guid.NewGuid());
	public ConnectorState State { get; private set; } = ConnectorState.Unspecified;
	public Task Stopped => _stoppedTcs.Task;

	public bool IsDisposed { get; private set; }
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

	public ValueTask DisposeAsync() {
		IsDisposed = true;
		State = ConnectorState.Stopped;

		_stoppedTcs.TrySetResult();
		return ValueTask.CompletedTask;
	}
}
