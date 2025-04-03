// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class deleting_stream<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private MiniNode<TLogFormat, TStreamId> _node;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		await _node.Shutdown();
		await base.TestFixtureTearDown();
	}

	virtual protected IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	[Test]
	[Category("Network")]
	public async Task which_doesnt_exists_should_success_when_passed_empty_stream_expected_version() {
		const string stream = "which_already_exists_should_success_when_passed_empty_stream_expected_version";
		using (var connection = BuildConnection(_node)) {
			await connection.ConnectAsync();
			await connection.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);
		}
	}

	[Test]
	[Category("Network")]
	public async Task which_doesnt_exists_should_success_when_passed_any_for_expected_version() {
		const string stream = "which_already_exists_should_success_when_passed_any_for_expected_version";
		using (var connection = BuildConnection(_node)) {
			await connection.ConnectAsync();

			await connection.DeleteStreamAsync(stream, ExpectedVersion.Any, hardDelete: true);
		}
	}

	[Test]
	[Category("Network")]
	public async Task with_invalid_expected_version_should_fail() {
		const string stream = "with_invalid_expected_version_should_fail";
		using (var connection = BuildConnection(_node)) {
			await connection.ConnectAsync();

			await AssertEx.ThrowsAsync<WrongExpectedVersionException>(() =>
				connection.DeleteStreamAsync(stream, 1, hardDelete: true));
		}
	}

	public async Task should_return_log_position_when_writing() {
		const string stream = "delete_should_return_log_position_when_writing";
		using (var connection = BuildConnection(_node)) {
			await connection.ConnectAsync();

			var result = await connection
				.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());
			var delete = await connection.DeleteStreamAsync(stream, 1, hardDelete: true);

			Assert.IsTrue(0 < result.LogPosition.PreparePosition);
			Assert.IsTrue(0 < result.LogPosition.CommitPosition);
		}
	}

	[Test]
	[Category("Network")]
	public async Task which_was_already_deleted_should_fail() {
		const string stream = "which_was_allready_deleted_should_fail";
		using (var connection = BuildConnection(_node)) {
			await connection.ConnectAsync();

			await connection.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			await AssertEx.ThrowsAsync<StreamDeletedException>(
				() => connection.DeleteStreamAsync(stream, ExpectedVersion.Any, hardDelete: true));
		}
	}
}
