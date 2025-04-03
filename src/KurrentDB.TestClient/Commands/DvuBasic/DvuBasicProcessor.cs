// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using EventStore.Client.Messages;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;
using OperationResult = EventStore.Client.Messages.OperationResult;

namespace KurrentDB.TestClient.Commands.DvuBasic;

internal class DvuBasicProcessor : ICmdProcessor {
	public string Keyword {
		get { return "verify"; }
	}

	public string Usage {
		get {
			return string.Format("{0} " +
			                     "<writers, default = 20> " +
			                     "<readers, default = 30> " +
			                     "<events, default = 1000000> " +
			                     "<streams per plugin, default = 1000> " +
			                     "<producers, default = [bank], available = [{1}]>",
				Keyword,
				string.Join(",", AvailableProducers));
		}
	}

	public IEnumerable<string> AvailableProducers {
		get { yield return "bank"; }
	}

	public IBasicProducer[] Producers { get; set; }

	private int _writers;
	private string[] _streams;
	private int[] _heads;

	private volatile bool _stopReading;
	private readonly object _factoryLock = new object();

	public bool Execute(CommandProcessorContext context, string[] args) {
		var writers = 20;
		var readers = 30;
		var events = 1000000;
		var streams = 1000;
		var producers = new[] {"bank"};

		if (args.Length != 0 && args.Length != 5) {
			context.Log.Error("Invalid number of arguments. Should be 0 or 5");
			return false;
		}

		if (args.Length > 0) {
			int writersArg;
			int readersArg;
			int eventsArg;
			int streamsArg;

			if (!int.TryParse(args[0], out writersArg)) {
				context.Log.Error("Invalid argument value for <writers>");
				return false;
			}

			if (!int.TryParse(args[1], out readersArg)) {
				context.Log.Error("Invalid argument value for <readers>");
				return false;
			}

			if (!int.TryParse(args[2], out eventsArg)) {
				context.Log.Error("Invalid argument value for <events>");
				return false;
			}

			if (!int.TryParse(args[3], out streamsArg)) {
				context.Log.Error("Invalid argument value for <streams>");
				return false;
			}

			string[] producersArg = args[4].Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries)
				.Select(p => p.Trim().ToLower()).Distinct().ToArray();
			if (producersArg.Length <= 0) {
				context.Log.Error("Invalid argument value for <plugins>");
				return false;
			}

			if (producersArg.Any(p => !AvailableProducers.Contains(p))) {
				context.Log.Error("Invalid producers argument. Pass comma-separated subset of [{producers}]",
					string.Join(",", AvailableProducers));
				return false;
			}

			writers = writersArg;
			readers = readersArg;
			events = eventsArg;
			streams = streamsArg;
			producers = producersArg;
		}

		_writers = writers;
		return InitProducers(producers) && Run(context, writers, readers, events, streams);
	}

	private bool InitProducers(string[] producers) {
		if (producers.Length == 1 && producers[0] == "bank") {
			Producers = new IBasicProducer[] {new BankAccountBasicProducer()};
			return true;
		}

		return false;
	}

	private bool Run(CommandProcessorContext context, int writers, int readers, int events, int streams) {
		context.IsAsync();

		_streams = new string[streams * Producers.Length];
		for (var i = 0; i < Producers.Length; i++) {
			for (var j = i * streams; j < streams * (i + 1); j++) {
				_streams[j] = StreamNamesGenerator.GenerateName(Producers[i].Name, j - i * streams);
			}
		}

		_heads = Enumerable.Repeat(-1, streams * Producers.Length).ToArray();

		return Verify(context, writers, readers, events);
	}

	private bool Verify(CommandProcessorContext context, int writers, int readers, int events) {
		var readStatuses = Enumerable.Range(0, readers).Select(x => new Status(context.Log)).ToArray();
		var readNotifications = Enumerable.Range(0, readers).Select(x => new ManualResetEventSlim(false)).ToArray();
		var writeStatuses = Enumerable.Range(0, writers).Select(x => new Status(context.Log)).ToArray();
		var writeNotifications =
			Enumerable.Range(0, writers).Select(x => new ManualResetEventSlim(false)).ToArray();

		for (int i = 0; i < readers; i++) {
			var i1 = i;
			var thread = new Thread(() => Read(readStatuses[i1], i1, context, readNotifications[i1]));
			thread.IsBackground = true;
			thread.Start();
		}

		for (int i = 0; i < writers; i++) {
			var i1 = i;
			var thread = new Thread(() =>
				Write(writeStatuses[i1], i1, context, events / writers, writeNotifications[i1]));
			thread.IsBackground = true;
			thread.Start();
		}

		foreach (var writeNotification in writeNotifications) {
			writeNotification.Wait();
		}

		_stopReading = true;
		foreach (var readNotification in readNotifications) {
			readNotification.Wait();
		}

		context.Log.Information("dvub finished execution : ");

		var writersTable = new ConsoleTable("WRITER ID", "Status");

		foreach (var ws in writeStatuses) {
			writersTable.AppendRow(ws.ThreadId.ToString(), ws.Success ? "Success" : "Fail");
		}

		var readersTable = new ConsoleTable("READER ID", "Status");
		foreach (var rs in readStatuses) {
			readersTable.AppendRow(rs.ThreadId.ToString(), rs.Success ? "Success" : "Fail");
		}

		context.Log.Information(writersTable.CreateIndentedTable());
		context.Log.Information(readersTable.CreateIndentedTable());

		var success = writeStatuses.All(s => s.Success) && readStatuses.All(s => s.Success);
		if (success)
			context.Success();
		else
			context.Fail();
		return success;
	}

	private void Write(Status status, int writerIdx, CommandProcessorContext context, int requests,
		ManualResetEventSlim finish) {
		TcpTypedConnection<byte[]> connection;
		var iteration = new AutoResetEvent(false);

		var sent = 0;

		var prepareTimeouts = 0;
		var commitTimeouts = 0;
		var forwardTimeouts = 0;
		var wrongExpectedVersion = 0;
		var streamsDeleted = 0;

		var failed = 0;

		var rnd = new Random(writerIdx);

		var streamIdx = -1;
		var head = -1;

		Action<TcpTypedConnection<byte[]>, TcpPackage> packageHandler = (conn, pkg) => {
			var dto = pkg.Data.Deserialize<WriteEventsCompleted>();
			switch (dto.Result) {
				case OperationResult.Success:
					lock (_heads) {
						var currentHead = _heads[streamIdx];
						Ensure.Equal(currentHead, head, "currentHead");
						_heads[streamIdx]++;
					}

					break;
				case OperationResult.PrepareTimeout:
					prepareTimeouts++;
					failed++;
					break;
				case OperationResult.CommitTimeout:
					commitTimeouts++;
					failed++;
					break;
				case OperationResult.ForwardTimeout:
					forwardTimeouts++;
					failed++;
					break;
				case OperationResult.WrongExpectedVersion:
					wrongExpectedVersion++;
					failed++;
					break;
				case OperationResult.StreamDeleted:
					streamsDeleted++;
					failed++;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			sent++;
			if (sent % 1000 == 0)
				status.ReportWritesProgress(writerIdx, sent, prepareTimeouts, commitTimeouts, forwardTimeouts,
					wrongExpectedVersion, streamsDeleted, failed, requests);
			iteration.Set();
		};

		Action<TcpTypedConnection<byte[]>> established = _ => { };
		Action<TcpTypedConnection<byte[]>, SocketError> closed = null;
		closed = (_, __) => {
			if (!context._tcpTestClient.Options.Reconnect) return;
			Thread.Sleep(TimeSpan.FromSeconds(1));
			connection =
				context._tcpTestClient.CreateTcpConnection(context, packageHandler, cn => iteration.Set(), closed, false);
		};

		connection = context._tcpTestClient.CreateTcpConnection(context, packageHandler, established, closed, false);

		for (var i = 0; i < requests; ++i) {
			streamIdx = NextStreamForWriting(rnd, writerIdx);
			lock (_heads) {
				head = _heads[streamIdx];
			}

			var evnt = CreateEvent(_streams[streamIdx], head + 1);
			var write = new WriteEvents(
				_streams[streamIdx],
				head,
				new[] {
					new NewEvent(evnt.EventId.ToByteArray(), evnt.EventType,
						evnt.IsJson ? 1 : 0, 0, evnt.Data, evnt.Metadata)
				},
				false);

			var package = new TcpPackage(TcpCommand.WriteEvents, Guid.NewGuid(), write.Serialize());
			connection.EnqueueSend(package.AsByteArray());
			iteration.WaitOne();
		}

		status.ReportWritesProgress(writerIdx, sent, prepareTimeouts, commitTimeouts, forwardTimeouts,
			wrongExpectedVersion, streamsDeleted, failed, requests);
		status.FinilizeStatus(writerIdx, failed != sent);
		context._tcpTestClient.Options.Reconnect = false;
		connection.Close();
		finish.Set();
	}

	private void Read(Status status, int readerIdx, CommandProcessorContext context,
		ManualResetEventSlim finishedEvent) {
		TcpTypedConnection<byte[]> connection;
		var iteration = new AutoResetEvent(false);

		var successes = 0;
		var fails = 0;

		var rnd = new Random(readerIdx);

		var streamIdx = -1;
		var eventidx = -1;

		Action<TcpTypedConnection<byte[]>, TcpPackage> packageReceived = (conn, pkg) => {
			var dto = pkg.Data.Deserialize<ReadEventCompleted>();
			switch (dto.Result) {
				case ReadEventCompleted.Types.ReadEventResult.Success:
					if (Equal(_streams[streamIdx], eventidx, dto.Event.Event.EventType, dto.Event.Event.Data.ToByteArray())) {
						successes++;
						if (successes % 1000 == 0)
							status.ReportReadsProgress(readerIdx, successes, fails);
					} else {
						fails++;
						status.ReportReadError(readerIdx, _streams[streamIdx], eventidx);
					}

					break;
				case ReadEventCompleted.Types.ReadEventResult.NotFound:
				case ReadEventCompleted.Types.ReadEventResult.NoStream:
				case ReadEventCompleted.Types.ReadEventResult.StreamDeleted:
				case ReadEventCompleted.Types.ReadEventResult.Error:
				case ReadEventCompleted.Types.ReadEventResult.AccessDenied:
					fails++;
					status.ReportNotFoundOnRead(readerIdx, _streams[streamIdx], eventidx);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			iteration.Set();
		};
		Action<TcpTypedConnection<byte[]>> established = _ => { };
		Action<TcpTypedConnection<byte[]>, SocketError> closed = null;
		closed = (_, __) => {
			if (!context._tcpTestClient.Options.Reconnect) return;
			Thread.Sleep(TimeSpan.FromSeconds(1));
			connection =
				context._tcpTestClient.CreateTcpConnection(context, packageReceived, cn => iteration.Set(), closed, false);
		};

		connection = context._tcpTestClient.CreateTcpConnection(context, packageReceived, established, closed, false);

		while (!_stopReading) {
			streamIdx = NextStreamForReading(rnd, readerIdx);
			int head;
			lock (_heads)
				head = _heads[streamIdx];

			if (head > 0) {
				eventidx = NextRandomEventVersion(rnd, head);
				var stream = _streams[streamIdx];
				var corrid = Guid.NewGuid();
				var read = new ReadEvent(stream, eventidx, resolveLinkTos: false,
					requireLeader: false);
				var package = new TcpPackage(TcpCommand.ReadEvent, corrid, read.Serialize());

				connection.EnqueueSend(package.AsByteArray());
				iteration.WaitOne();
			} else
				Thread.Sleep(100);
		}

		status.ReportReadsProgress(readerIdx, successes, fails);
		status.FinilizeStatus(readerIdx, fails == 0);
		context._tcpTestClient.Options.Reconnect = false;
		connection.Close();
		finishedEvent.Set();
	}

	private int NextStreamForWriting(Random rnd, int writerIdx) {
		if (_writers >= _streams.Length) {
			if (_writers > _streams.Length)
				return writerIdx % _streams.Length;

			return writerIdx;
		}

		return rnd.Next(_streams.Length);
	}

	private int NextStreamForReading(Random rnd, int readerIdx) {
		return rnd.Next(_streams.Length);
	}

	private int NextRandomEventVersion(Random rnd, int head) {
		return head % 2 == 0 ? head : rnd.Next(1, head);
	}

	private IBasicProducer CorrespondingProducer(string stream) {
		return Producers.Single(f => string.Equals(f.Name, stream, StringComparison.OrdinalIgnoreCase));
	}

	private Event CreateEvent(string stream, int version) {
		var originalName = StreamNamesGenerator.GetOriginalName(stream);
		var factory = CorrespondingProducer(originalName);

		Event generated;
		lock (_factoryLock)
			generated = factory.Create(version);

		return generated;
	}

	private bool Equal(string stream, int expectedIdx, string eventType, byte[] actual) {
		var originalName = StreamNamesGenerator.GetOriginalName(stream);
		var producer = CorrespondingProducer(originalName);

		bool equal;
		lock (_factoryLock)
			equal = producer.Equal(expectedIdx, eventType, actual);

		return equal;
	}
}
