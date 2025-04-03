// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
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
public class append_to_stream<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private readonly TcpType _tcpType = TcpType.Ssl;
	private MiniNode<TLogFormat, TStreamId> _node;

	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.To(node, _tcpType);
	}

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

	[Test, Category("Network")]
	public async Task should_allow_appending_zero_events_to_stream_with_no_problems() {
		const string stream1 = "should_allow_appending_zero_events_to_stream_with_no_problems1";
		const string stream2 = "should_allow_appending_zero_events_to_stream_with_no_problems2";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			Assert.AreEqual(-1,
				(await store.AppendToStreamAsync(stream1, ExpectedVersion.Any)).NextExpectedVersion);
			Assert.AreEqual(-1,
				(await store.AppendToStreamAsync(stream1, ExpectedVersion.NoStream)).NextExpectedVersion);
			Assert.AreEqual(-1, (await store.AppendToStreamAsync(stream1, ExpectedVersion.Any)).NextExpectedVersion);
			Assert.AreEqual(-1,
				(await store.AppendToStreamAsync(stream1, ExpectedVersion.NoStream)).NextExpectedVersion);

			var read1 = await store.ReadStreamEventsForwardAsync(stream1, 0, 2, resolveLinkTos: false);
			Assert.That(read1.Events.Length, Is.EqualTo(0));

			Assert.AreEqual(-1, (await store.AppendToStreamAsync(stream2, ExpectedVersion.NoStream)).NextExpectedVersion);
			Assert.AreEqual(-1, (await store.AppendToStreamAsync(stream2, ExpectedVersion.Any)).NextExpectedVersion);
			Assert.AreEqual(-1, (await store.AppendToStreamAsync(stream2, ExpectedVersion.NoStream)).NextExpectedVersion);
			Assert.AreEqual(-1, (await store.AppendToStreamAsync(stream2, ExpectedVersion.Any)).NextExpectedVersion);

			var read2 = await store.ReadStreamEventsForwardAsync(stream2, 0, 2, resolveLinkTos: false);
			Assert.That(read2.Events.Length, Is.EqualTo(0));
		}
	}

	[Test, Category("Network")]
	public async Task should_create_stream_with_no_stream_exp_ver_on_first_write_if_does_not_exist() {
		const string stream = "should_create_stream_with_no_stream_exp_ver_on_first_write_if_does_not_exist";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			Assert.AreEqual(0, (await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent()))
				.NextExpectedVersion);

			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 2, resolveLinkTos: false);
			Assert.That(read.Events.Length, Is.EqualTo(1));
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_create_stream_with_any_exp_ver_on_first_write_if_does_not_exist() {
		const string stream = "should_create_stream_with_any_exp_ver_on_first_write_if_does_not_exist";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0, (await store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent())).NextExpectedVersion);

			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 2, resolveLinkTos: false);
			Assert.That(read.Events.Length, Is.EqualTo(1));
		}
	}

	[Test]
	[Category("Network")]
	public async Task multiple_idempotent_writes() {
		const string stream = "multiple_idempotent_writes";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = new[] {
				TestEvent.NewTestEvent(), TestEvent.NewTestEvent(), TestEvent.NewTestEvent(),
				TestEvent.NewTestEvent()
			};
			Assert.AreEqual(3,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.Any, events)).NextExpectedVersion);
			Assert.AreEqual(3,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.Any, events)).NextExpectedVersion);
		}
	}

	[Test]
	[Category("Network")]
	public async Task multiple_idempotent_writes_with_same_id_bug_case() {
		const string stream = "multiple_idempotent_writes_with_same_id_bug_case";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var x = TestEvent.NewTestEvent();
			var events = new[] { x, x, x, x, x, x };
			Assert.AreEqual(5, (await store.AppendToStreamAsync(stream, ExpectedVersion.Any, events)).NextExpectedVersion);
		}
	}

	[Test]
	[Category("Network")]
	public async Task
		in_case_where_multiple_writes_of_multiple_events_with_the_same_ids_using_expected_version_any_then_next_expected_version_is_unreliable() {
		const string stream = "in_wtf_multiple_case_of_multiple_writes_expected_version_any_per_all_same_id";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var x = TestEvent.NewTestEvent();
			var events = new[] { x, x, x, x, x, x };
			Assert.AreEqual(5, (await store.AppendToStreamAsync(stream, ExpectedVersion.Any, events)).NextExpectedVersion);
			var f = await store.AppendToStreamAsync(stream, ExpectedVersion.Any, events);
			Assert.AreEqual(0, f.NextExpectedVersion);
		}
	}

	[Test]
	[Category("Network")]
	public async Task
		in_case_where_multiple_writes_of_multiple_events_with_the_same_ids_using_expected_version_nostream_then_next_expected_version_is_correct() {
		const string stream =
			"in_slightly_reasonable_multiple_case_of_multiple_writes_with_expected_version_per_all_same_id";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var x = TestEvent.NewTestEvent();
			var events = new[] { x, x, x, x, x, x };
			Assert.AreEqual(5,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, events)).NextExpectedVersion);
			var f = await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, events);
			Assert.AreEqual(5, f.NextExpectedVersion);
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_fail_writing_with_correct_exp_ver_to_deleted_stream() {
		const string stream = "should_fail_writing_with_correct_exp_ver_to_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			await AssertEx.ThrowsAsync<StreamDeletedException>(
				() => store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent()));

		}
	}

	[Test]
	[Category("Network")]
	public async Task should_return_log_position_when_writing() {
		const string stream = "should_return_log_position_when_writing";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var result = await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());
			Assert.IsTrue(0 < result.LogPosition.PreparePosition);
			Assert.IsTrue(0 < result.LogPosition.CommitPosition);
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_fail_writing_with_any_exp_ver_to_deleted_stream() {
		const string stream = "should_fail_writing_with_any_exp_ver_to_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			try {
				await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);
			} catch (Exception exc) {
				Console.WriteLine(exc);
				Assert.Fail();
			}

			await AssertEx.ThrowsAsync<StreamDeletedException>(
				() => store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent()));
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_fail_writing_with_invalid_exp_ver_to_deleted_stream() {
		const string stream = "should_fail_writing_with_invalid_exp_ver_to_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			await AssertEx.ThrowsAsync<StreamDeletedException>(() => store.AppendToStreamAsync(stream, 5, TestEvent.NewTestEvent()));
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_append_with_correct_exp_ver_to_existing_stream() {
		const string stream = "should_append_with_correct_exp_ver_to_existing_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());

			await store.AppendToStreamAsync(stream, 0, new[] { TestEvent.NewTestEvent() });
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_append_with_any_exp_ver_to_existing_stream() {
		const string stream = "should_append_with_any_exp_ver_to_existing_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0, (await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent())).NextExpectedVersion);
			Assert.AreEqual(1, (await store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent())).NextExpectedVersion);
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_fail_appending_with_wrong_exp_ver_to_existing_stream() {
		const string stream = "should_fail_appending_with_wrong_exp_ver_to_existing_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var wev = await AssertEx.ThrowsAsync<WrongExpectedVersionException>(() =>
				store.AppendToStreamAsync(stream, 1, new[] { TestEvent.NewTestEvent() }));
			Assert.AreEqual(1, wev.ExpectedVersion);
			Assert.AreEqual(ExpectedVersion.NoStream, wev.ActualVersion);
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_append_with_stream_exists_exp_ver_to_existing_stream() {
		const string stream = "should_append_with_stream_exists_exp_ver_to_existing_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());

			await store.AppendToStreamAsync(stream, ExpectedVersion.StreamExists, TestEvent.NewTestEvent());
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_append_with_stream_exists_exp_ver_to_stream_with_multiple_events() {
		const string stream = "should_append_with_stream_exists_exp_ver_to_stream_with_multiple_events";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			for (var i = 0; i < 5; i++) {
				await store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent());
			}

			await store.AppendToStreamAsync(stream, ExpectedVersion.StreamExists, TestEvent.NewTestEvent());
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_append_with_stream_exists_exp_ver_if_metadata_stream_exists() {
		const string stream = "should_append_with_stream_exists_exp_ver_if_metadata_stream_exists";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			await store.SetStreamMetadataAsync(stream, ExpectedVersion.Any,
				new StreamMetadata(10, null, null, null, null));

			await store.AppendToStreamAsync(stream, ExpectedVersion.StreamExists, TestEvent.NewTestEvent());
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_fail_appending_with_stream_exists_exp_ver_and_stream_does_not_exist() {
		const string stream = "should_fail_appending_with_stream_exists_exp_ver_and_stream_does_not_exist";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var wev = await AssertEx.ThrowsAsync<WrongExpectedVersionException>(
				() => store.AppendToStreamAsync(stream, ExpectedVersion.StreamExists, TestEvent.NewTestEvent()));
			Assert.AreEqual(ExpectedVersion.StreamExists, wev.ExpectedVersion);
			Assert.AreEqual(ExpectedVersion.NoStream, wev.ActualVersion);
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_fail_appending_with_stream_exists_exp_ver_to_hard_deleted_stream() {
		const string stream = "should_fail_appending_with_stream_exists_exp_ver_to_hard_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			await AssertEx.ThrowsAsync<StreamDeletedException>(
				() => store.AppendToStreamAsync(stream, ExpectedVersion.StreamExists, TestEvent.NewTestEvent()));
		}
	}

	[Test]
	[Category("Network")]
	public async Task should_fail_appending_with_stream_exists_exp_ver_to_soft_deleted_stream() {
		const string stream = "should_fail_appending_with_stream_exists_exp_ver_to_soft_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());

			await store.DeleteStreamAsync(stream, ExpectedVersion.StreamExists, hardDelete: false);

			await AssertEx.ThrowsAsync<StreamDeletedException>(
				() => store.AppendToStreamAsync(stream, ExpectedVersion.StreamExists, TestEvent.NewTestEvent()));
		}
	}

	[Test, Category("Network")]
	public async Task can_append_multiple_events_at_once() {
		const string stream = "can_append_multiple_events_at_once";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 100).Select(i => TestEvent.NewTestEvent(i.ToString(), i.ToString()));
			Assert.AreEqual(99, (await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, events)).NextExpectedVersion);
		}
	}

	[Test]
	public async Task cannot_append_multiple_events_larger_than_chunk_size_at_once() {
		const string stream = "cannot_append_multiple_events_larger_than_chunk_size_at_once";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var largeData = new string(' ', 20000);
			var events = Enumerable.Range(0, 100).Select(i => TestEvent.NewTestEvent(largeData, i.ToString()));

			Assert.AreEqual(3, (await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, events.Take(4))).NextExpectedVersion);

			Assert.ThrowsAsync<InvalidTransactionException>(async () =>
				await store.AppendToStreamAsync(stream, 3, events));

			// can still write to the stream with ExpectedVersion 3
			Assert.AreEqual(7, (await store.AppendToStreamAsync(stream, 3, events.Take(4))).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_to_stream_with_long_name() {
		var stream = "can_append_to_stream_with_long_name" + new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent())).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_multiple_events_to_stream_with_long_name_at_once() {
		var stream = "can_append_multiple_events_to_stream_with_long_name_at_once" + new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var events = Enumerable.Range(0, 3).Select(i => TestEvent.NewTestEvent(i.ToString(), i.ToString()));
			Assert.AreEqual(2, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, events)).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_event_with_long_event_type() {
		const string stream = "can_append_event_with_long_event_type";
		var eventType = new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent(eventName: eventType))).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_multiple_events_with_long_event_type_at_once() {
		const string stream = "can_append_multiple_events_with_long_event_type_at_once";
		var eventType = new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var events = Enumerable.Range(0, 3).Select(i =>
				TestEvent.NewTestEvent(i.ToString(), i.ToString(), eventName: eventType));
			Assert.AreEqual(2, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, events)).NextExpectedVersion);
		}
	}

	[Test, Category("Network")]
	public async Task returns_failure_status_when_conditionally_appending_with_version_mismatch() {
		const string stream = "returns_failure_status_when_conditionally_appending_with_version_mismatch";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var result = await store.ConditionalAppendToStreamAsync(stream, 7, new[] { TestEvent.NewTestEvent() });

			Assert.AreEqual(ConditionalWriteStatus.VersionMismatch, result.Status);
		}
	}

	[Test, Category("Network")]
	public async Task returns_success_status_when_conditionally_appending_with_matching_version() {
		const string stream = "returns_success_status_when_conditionally_appending_with_matching_version";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var result = await store
				.ConditionalAppendToStreamAsync(stream, ExpectedVersion.Any, new[] { TestEvent.NewTestEvent() });

			Assert.AreEqual(ConditionalWriteStatus.Succeeded, result.Status);
			Assert.IsNotNull(result.LogPosition);
			Assert.IsNotNull(result.NextExpectedVersion);
		}
	}

	[Test, Category("Network")]
	public async Task returns_failure_status_when_conditionally_appending_to_a_deleted_stream() {
		const string stream = "returns_failure_status_when_conditionally_appending_to_a_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent());
			await store.DeleteStreamAsync(stream, ExpectedVersion.Any, true);

			var result = await store
				.ConditionalAppendToStreamAsync(stream, ExpectedVersion.Any, new[] { TestEvent.NewTestEvent() });

			Assert.AreEqual(ConditionalWriteStatus.StreamDeleted, result.Status);
		}
	}
}

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class ssl_append_to_stream<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private readonly TcpType _tcpType = TcpType.Ssl;
	protected MiniNode<TLogFormat, TStreamId> _node;

	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.To(node, _tcpType);
	}


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

	[Test]
	public async Task should_allow_appending_zero_events_to_stream_with_no_problems() {
		const string stream = "should_allow_appending_zero_events_to_stream_with_no_problems";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(-1,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream)).NextExpectedVersion);

			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 2, resolveLinkTos: false);
			Assert.That(read.Events.Length, Is.EqualTo(0));
		}
	}

	[Test]
	public async Task should_create_stream_with_no_stream_exp_ver_on_first_write_if_does_not_exist() {
		const string stream = "should_create_stream_with_no_stream_exp_ver_on_first_write_if_does_not_exist";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent())).NextExpectedVersion);

			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 2, resolveLinkTos: false);
			Assert.That(read.Events.Length, Is.EqualTo(1));
		}
	}

	[Test]
	public async Task should_create_stream_with_any_exp_ver_on_first_write_if_does_not_exist() {
		const string stream = "should_create_stream_with_any_exp_ver_on_first_write_if_does_not_exist";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent())).NextExpectedVersion);

			var read = await store.ReadStreamEventsForwardAsync(stream, 0, 2, resolveLinkTos: false);
			Assert.That(read.Events.Length, Is.EqualTo(1));
		}
	}

	[Test]
	public async Task should_fail_writing_with_correct_exp_ver_to_deleted_stream() {
		const string stream = "should_fail_writing_with_correct_exp_ver_to_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			await AssertEx.ThrowsAsync<StreamDeletedException>(() =>
				store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent()));
		}
	}

	[Test]
	public async Task should_fail_writing_with_any_exp_ver_to_deleted_stream() {
		const string stream = "should_fail_writing_with_any_exp_ver_to_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			await AssertEx.ThrowsAsync<StreamDeletedException>(() =>
				store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent()));
		}
	}

	[Test]
	public async Task should_fail_writing_with_invalid_exp_ver_to_deleted_stream() {
		const string stream = "should_fail_writing_with_invalid_exp_ver_to_deleted_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			await store.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

			await AssertEx.ThrowsAsync<StreamDeletedException>(() =>
				store.AppendToStreamAsync(stream, 5, new[] { TestEvent.NewTestEvent() }));
		}
	}

	[Test]
	public async Task should_append_with_correct_exp_ver_to_existing_stream() {
		const string stream = "should_append_with_correct_exp_ver_to_existing_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent())).NextExpectedVersion);
			Assert.AreEqual(1, (await store.AppendToStreamAsync(stream, 0, TestEvent.NewTestEvent())).NextExpectedVersion);
		}
	}

	[Test]
	public async Task should_append_with_any_exp_ver_to_existing_stream() {
		const string stream = "should_append_with_any_exp_ver_to_existing_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent())).NextExpectedVersion);
			Assert.AreEqual(1, (await store.AppendToStreamAsync(stream, ExpectedVersion.Any, TestEvent.NewTestEvent())).NextExpectedVersion);
		}
	}

	[Test]
	public async Task should_return_log_position_when_writing() {
		const string stream = "should_return_log_position_when_writing";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var result = await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());
			Assert.IsTrue(0 < result.LogPosition.PreparePosition);
			Assert.IsTrue(0 < result.LogPosition.CommitPosition);
		}
	}

	[Test]
	public async Task should_fail_appending_with_wrong_exp_ver_to_existing_stream() {
		const string stream = "should_fail_appending_with_wrong_exp_ver_to_existing_stream";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0,
				(await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent())).NextExpectedVersion);

			var wev = await AssertEx.ThrowsAsync<WrongExpectedVersionException>(() =>
				store.AppendToStreamAsync(stream, 1, TestEvent.NewTestEvent()));
			Assert.AreEqual(1, wev.ExpectedVersion);
			Assert.AreEqual(0, wev.ActualVersion.GetValueOrDefault(0));
		}
	}

	[Test]
	public async Task can_append_multiple_events_at_once() {
		const string stream = "can_append_multiple_events_at_once";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var events = Enumerable.Range(0, 100).Select(i => TestEvent.NewTestEvent(i.ToString(), i.ToString()));
			Assert.AreEqual(99, (await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, events)).NextExpectedVersion);
		}
	}

	[Test]
	public async Task cannot_append_multiple_events_larger_than_chunk_size_at_once() {
		const string stream = "cannot_append_multiple_events_larger_than_chunk_size_at_once";
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			var largeData = new string(' ', 20000);
			var events = Enumerable.Range(0, 100).Select(i => TestEvent.NewTestEvent(largeData, i.ToString()));

			Assert.AreEqual(3, (await store.AppendToStreamAsync(stream, ExpectedVersion.NoStream, events.Take(4))).NextExpectedVersion);

			Assert.ThrowsAsync<InvalidTransactionException>(async () =>
				await store.AppendToStreamAsync(stream, 3, events));

			// can still write to the stream with ExpectedVersion 3
			Assert.AreEqual(7, (await store.AppendToStreamAsync(stream, 3, events.Take(4))).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_to_stream_with_long_name() {
		var stream = "can_append_to_stream_with_long_name" + new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent())).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_multiple_events_to_stream_with_long_name_at_once() {
		var stream = "can_append_multiple_events_to_stream_with_long_name_at_once" + new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var events = Enumerable.Range(0, 3).Select(i => TestEvent.NewTestEvent(i.ToString(), i.ToString()));
			Assert.AreEqual(2, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, events)).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_event_with_long_event_type() {
		const string stream = "can_append_event_with_long_event_type";
		var eventType = new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			Assert.AreEqual(0, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent(eventName: eventType))).NextExpectedVersion);
		}
	}

	[Test]
	public async Task can_append_multiple_events_with_long_event_type_at_once() {
		const string stream = "can_append_multiple_events_with_long_event_type_at_once";
		var eventType = new string('A', 300);
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();
			var events = Enumerable.Range(0, 3).Select(i =>
				TestEvent.NewTestEvent(i.ToString(), i.ToString(), eventName: eventType));
			Assert.AreEqual(2, (await store.AppendToStreamAsync
				(stream, ExpectedVersion.NoStream, events)).NextExpectedVersion);
		}
	}
}
