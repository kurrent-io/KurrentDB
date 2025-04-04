// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class isjson_flag_on_event<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private MiniNode<TLogFormat, TStreamId> _node;

	[SetUp]
	public override async Task SetUp() {
		await base.SetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();
	}

	[TearDown]
	public override async Task TearDown() {
		await _node.Shutdown();
		await base.TearDown();
	}

	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.To(node, TcpType.Ssl);
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task should_be_preserved_with_all_possible_write_and_read_methods() {
		const string stream = "should_be_preserved_with_all_possible_write_methods";
		using (var connection = BuildConnection(_node)) {
			await connection.ConnectAsync();

			await connection.AppendToStreamAsync(
					stream,
					ExpectedVersion.Any,
					new EventData(Guid.NewGuid(), "some-type", true,
						Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}"), null),
					new EventData(Guid.NewGuid(), "some-type", true, null,
						Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}")),
					new EventData(Guid.NewGuid(), "some-type", true,
						Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}"),
						Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}")));
			var expectedEvents = 3;

			if (LogFormatHelper<TLogFormat, TStreamId>.SupportsExplicitTransactions) {
				using (var transaction = await connection.StartTransactionAsync(stream, ExpectedVersion.Any)) {
					await transaction.WriteAsync(
						new EventData(Guid.NewGuid(), "some-type", true,
							Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}"), null),
						new EventData(Guid.NewGuid(), "some-type", true, null,
							Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}")),
						new EventData(Guid.NewGuid(), "some-type", true,
							Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}"),
							Helper.UTF8NoBom.GetBytes("{\"some\":\"json\"}")));
					await transaction.CommitAsync();
					expectedEvents += 3;
				}
			}

			var done = new ManualResetEventSlim();
			_node.Node.MainQueue.Publish(new ClientMessage.ReadStreamEventsForward(
				Guid.NewGuid(), Guid.NewGuid(), new CallbackEnvelope(message => {
					Assert.IsInstanceOf<ClientMessage.ReadStreamEventsForwardCompleted>(message);
					var msg = (ClientMessage.ReadStreamEventsForwardCompleted)message;
					Assert.AreEqual(Data.ReadStreamResult.Success, msg.Result);
					Assert.AreEqual(expectedEvents, msg.Events.Count);
					Assert.IsTrue(msg.Events.All(x => (x.OriginalEvent.Flags & PrepareFlags.IsJson) != 0));

					done.Set();
				}), stream, 0, 100, false, false, null, null, replyOnExpired: false));
			Assert.IsTrue(done.Wait(10000), "Read was not completed in time.");
		}
	}
}
