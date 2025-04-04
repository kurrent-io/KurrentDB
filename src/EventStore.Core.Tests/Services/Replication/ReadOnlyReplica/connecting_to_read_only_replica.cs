// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Integration;
using NUnit.Framework;

namespace EventStore.Core.Tests.Replication.ReadOnlyReplica;

[Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class connecting_to_read_only_replica<TLogFormat, TStreamId> : specification_with_cluster<TLogFormat, TStreamId> {
	protected override MiniClusterNode<TLogFormat, TStreamId> CreateNode(int index, Endpoints endpoints, EndPoint[] gossipSeeds,
		bool wait = true) {
		var isReadOnly = index == 2;
		var node = new MiniClusterNode<TLogFormat, TStreamId>(
			PathName, index, endpoints.InternalTcp,
			endpoints.ExternalTcp, endpoints.HttpEndPoint, gossipSeeds, inMemDb: false,
			readOnlyReplica: isReadOnly);
		if (wait && !isReadOnly)
			WaitIdle();
		return node;
	}

	protected override IEventStoreConnection CreateConnection() {
		var settings = ConnectionSettings.Create()
			.DisableServerCertificateValidation()
			.PerformOnAnyNode();
		return EventStoreConnection.Create(settings, _nodes[2].ExternalTcpEndPoint);
	}

	[Test]
	public async Task append_to_stream_should_fail_with_not_supported_exception() {
		const string stream = "append_to_stream_should_fail_with_not_supported_exception";
		await AssertEx.ThrowsAsync<OperationNotSupportedException>(
			() => _conn.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent()));
	}

	[Test]
	public async Task delete_stream_should_fail_with_not_supported_exception() {
		const string stream = "delete_stream_should_fail_with_not_supported_exception";
		await AssertEx.ThrowsAsync<OperationNotSupportedException>(() =>
			_conn.DeleteStreamAsync(stream, ExpectedVersion.Any));
	}

	[Test]
	public async Task start_transaction_should_fail_with_not_supported_exception() {
		const string stream = "start_transaction_should_fail_with_not_supported_exception";
		await AssertEx.ThrowsAsync<OperationNotSupportedException>(() =>
			_conn.StartTransactionAsync(stream, ExpectedVersion.Any));
	}
}
