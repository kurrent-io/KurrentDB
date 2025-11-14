// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.DataProtection;
using KurrentDB.Connectors.Infrastructure.Connect.Components.Connectors;
using KurrentDB.Connectors.Management.Contracts.Events;
using KurrentDB.Connectors.Management.Contracts.Queries;
using KurrentDB.Connectors.Planes.Management.Data;
using KurrentDB.Connectors.Planes.Management.Queries;

namespace KurrentDB.Connectors.Tests.Planes.Management;

public class ConnectorQueriesTests(ITestOutputHelper output, ConnectorsAssemblyFixture fixture)
	: ConnectorsIntegrationTests(output, fixture) {
	IConnectorDataProtector CreateDataProtector() => new ConnectorsMasterDataProtector(
		Fixture.DataProtector,
		new DataProtectionOptions { Token = DataProtectionConstants.NoOpToken }
	);

	[Fact]
	public Task list_returns_all_connectors_when_no_paging_specified() => Fixture.TestWithTimeout(async cancellator => {
		// Arrange
		var streamId = Fixture.NewIdentifier();
		await SetupConnectorsSnapshot(streamId, connectorCount: 5, cancellator.Token);

		var queries = new ConnectorQueries(Fixture.NewReader, CreateDataProtector(), streamId);

		// Act
		var result = await queries.List(new ListConnectors(), cancellator.Token);

		// Assert
		result.Items.Should().HaveCount(5);
		result.TotalSize.Should().Be(5);
	});

	[Theory]
	[InlineData(10, 1, 5, 0, 5, 5)] // First page of 5 from 10 items
	[InlineData(10, 2, 5, 5, 5, 5)] // Second page of 5 from 10 items
	[InlineData(7, 2, 3, 3, 3, 3)] // Second page of 3 from 7 items
	[InlineData(8, 3, 3, 6, 2, 2)] // Third page of 3 from 8 items (partial)
	[InlineData(5, 3, 2, 4, 1, 1)] // Third page of 2 from 5 items (last item)
	[InlineData(5, 4, 2, 6, 0, 0)] // Fourth page of 2 from 5 items (no items)
	public Task list_pagination_returns_correct_items(
		int totalConnectors, int page, int pageSize, int expectedSkip, int expectedCount, int expectedTotalSize
	) => Fixture.TestWithTimeout(async cancellator => {
		// Arrange
		var streamId = Fixture.NewIdentifier();
		var connectorIds = await SetupConnectorsSnapshot(streamId, totalConnectors, cancellator.Token);

		var queries = new ConnectorQueries(Fixture.NewReader, CreateDataProtector(), streamId);

		// Act
		var query = new ListConnectors {
			Paging = new Paging { Page = page, PageSize = pageSize }
		};

		var result = await queries.List(query, cancellator.Token);

		// Assert
		result.Items.Should().HaveCount(expectedCount);
		result.Items.Select(x => x.ConnectorId).Should().BeEquivalentTo(connectorIds.Skip(expectedSkip).Take(expectedCount));
		result.TotalSize.Should().Be(expectedTotalSize);
	});

	async Task<List<string>> SetupConnectorsSnapshot(string streamId, int connectorCount, CancellationToken cancellationToken) {
		var projection = new ConnectorsStateProjection(Fixture.SnapshotProjectionsStore, streamId);
		var connectorIds = new List<string>();

		for (int i = 1; i <= connectorCount; i++) {
			var connectorId = $"connector-{i:D3}";
			connectorIds.Add(connectorId);

			var record = await Fixture.CreateRecord(
				new ConnectorCreated {
					ConnectorId = connectorId,
					Name = $"Connector {i}",
					Settings = {
						{ "instanceTypeName", "test-connector" }
					},
					Timestamp = Fixture.TimeProvider.GetUtcNow().ToTimestamp()
				}
			);

			await projection.ProcessRecord(Fixture.CreateRecordContext(connectorId, cancellationToken) with {
				Record = record
			});
		}

		return connectorIds;
	}
}
