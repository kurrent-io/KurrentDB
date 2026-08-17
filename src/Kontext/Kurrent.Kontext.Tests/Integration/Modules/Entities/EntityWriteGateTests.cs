// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Resolution;

namespace Kurrent.Kontext.Tests.Integration.Modules.Entities;

/// <summary>
/// The serialization seam between the entity projector's batch loop and offline resolution: one
/// bound connection, one turn at a time, and nothing at all when no projector is running.
/// </summary>
[Category("Integration")]
[Category("Entities")]
[Timeout(30_000)]
public class EntityWriteGateTests {
	[Test]
	public async ValueTask a_turn_runs_on_the_bound_connection(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		using var gate = new EntityWriteGate();

		// Act
		await Assert.That(gate.IsBound).IsFalse();

		using var binding = gate.Bind(connection);

		var same = await gate.RunAsync((bound, _) => ValueTask.FromResult(ReferenceEquals(bound, connection)), cancellationToken);

		// Assert
		await Assert.That(gate.IsBound).IsTrue();
		await Assert.That(same).IsTrue();
	}

	[Test]
	public async ValueTask turns_never_overlap(CancellationToken cancellationToken) {
		// Arrange — a turn that will not finish until the test lets it.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		using var gate    = new EntityWriteGate();
		using var binding = gate.Bind(connection);

		var entered = new TaskCompletionSource();
		var release = new TaskCompletionSource();

		var batchTurn = gate.RunAsync(
			async (_, _) => {
				entered.SetResult();
				await release.Task;
			}, cancellationToken);

		await entered.Task;

		// Act — resolution asks for a turn while the batch turn holds it.
		var resolutionRan  = false;
		var resolutionTurn = gate.RunAsync(
			(_, _) => {
				resolutionRan = true;
				return ValueTask.CompletedTask;
			}, cancellationToken);

		await Task.Delay(50, cancellationToken);

		// Assert — it waits, then runs once the batch turn lets go.
		await Assert.That(resolutionRan).IsFalse();

		release.SetResult();

		await batchTurn;
		await resolutionTurn;

		await Assert.That(resolutionRan).IsTrue();
	}

	[Test]
	public async ValueTask a_turn_without_a_projector_is_refused(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		using var gate = new EntityWriteGate();

		// Act + Assert — nothing bound yet: there is no write surface to lend.
		await Assert.That(async () => await gate.RunAsync((_, _) => ValueTask.CompletedTask, cancellationToken))
			.Throws<InvalidOperationException>();

		// Act + Assert — and none again once the projector's loop has ended.
		gate.Bind(connection).Dispose();

		await Assert.That(gate.IsBound).IsFalse();
		await Assert.That(async () => await gate.RunAsync((_, _) => ValueTask.CompletedTask, cancellationToken))
			.Throws<InvalidOperationException>();
	}

	[Test]
	public async ValueTask a_second_binding_is_refused(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		using var gate    = new EntityWriteGate();
		using var binding = gate.Bind(connection);

		// Act + Assert — a second bound connection would be a second writer.
		await Assert.That(() => gate.Bind(connection)).Throws<InvalidOperationException>();
	}
}
