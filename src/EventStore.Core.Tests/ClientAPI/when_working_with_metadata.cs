// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using ExpectedVersion = EventStore.ClientAPI.ExpectedVersion;
using StreamMetadata = EventStore.ClientAPI.StreamMetadata;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_working_with_metadata<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private MiniNode<TLogFormat, TStreamId> _node;
	private IEventStoreConnection _connection;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();

		_connection = BuildConnection(_node);
		await _connection.ConnectAsync();
	}

	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		_connection.Close();
		await _node.Shutdown();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task when_getting_metadata_for_an_existing_stream_and_no_metadata_exists() {
		const string stream = "when_getting_metadata_for_an_existing_stream_and_no_metadata_exists";

		await _connection.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());

		var meta = await _connection.GetStreamMetadataAsRawBytesAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(-1, meta.MetastreamVersion);
		Assert.AreEqual(Helper.UTF8NoBom.GetBytes(""), meta.StreamMetadata);
	}
}
