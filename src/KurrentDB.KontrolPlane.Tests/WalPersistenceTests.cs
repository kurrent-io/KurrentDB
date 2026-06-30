// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using KurrentDB.Core.XUnit.Tests;

namespace KurrentDB.KontrolPlane;

[Collection("RaftKontroller")]
public sealed class WalPersistenceTests : DirectoryFixture<WalPersistenceTests> {
	private const int SnapshotDepth = 10;

	[Fact]
	public async Task SnapshotPersistence() {
		await using (var kontroller = new RaftKontroller(new RaftKontroller.Options {
			             ListenAddress = new(IPAddress.Loopback, 3269),
			             AppointmentDuration = TimeSpan.FromDays(1), // elect leader just once
			             ConnectionPoolCapacity = 10,
			             WalOptions = new() {
				             Location = Directory,
			             },
			             SingleNodeDeployment = true,
			             SnapshotDepth = SnapshotDepth,
		             }) {
			             DataPlane = new TestDataPlane(),
		             }) {

			await kontroller.StartAsync(TestToken);

			// Add databases to trigger the snapshot construction
			for (var i = 0; i < SnapshotDepth + 2; i++) {
				await kontroller.AddOrUpdateDatabaseAsync(new Database { Id = i.ToString() });
			}

			await kontroller.StopAsync(TestToken);
		}

		// Recover persistent state
		await using (var kontroller = new RaftKontroller(new RaftKontroller.Options {
			             ListenAddress = new(IPAddress.Loopback, 3269),
			             AppointmentDuration = TimeSpan.FromDays(1), // elect leader just once
			             ConnectionPoolCapacity = 10,
			             WalOptions = new() {
				             Location = Directory,
			             },
			             SingleNodeDeployment = true,
			             SnapshotDepth = SnapshotDepth,
		             }) {
			             DataPlane = new TestDataPlane(),
		             }) {

			await kontroller.StartAsync(TestToken);
			var databases = await kontroller.GetDatabasesAsync(TestToken);
			for (var i = 0; i < SnapshotDepth + 2; i++) {
				Assert.Contains(i.ToString(), databases);
			}
		}
	}

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private sealed class TestDataPlane : IDataPlane {
		ValueTask<ReplicaState> IDataPlane.GetReplicaStateAsync(EndPoint address, CancellationToken token)
			=> ValueTask.FromException<ReplicaState>(new NotSupportedException());
	}
}
