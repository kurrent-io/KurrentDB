// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Projections;
using EventStore.ClientAPI.SystemData;
using EventStore.Core.Services;
using ConsoleLogger = EventStore.ClientAPI.Common.Log.ConsoleLogger;
using ILogger = Serilog.ILogger;
using TcpCommand = EventStore.Core.Services.Transport.Tcp.TcpCommand;
using TcpPackage = EventStore.Core.Services.Transport.Tcp.TcpPackage;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal abstract class ScenarioBase : IScenario {
	protected static readonly ILogger Log = Serilog.Log.ForContext<ScenarioBase>();

	protected static readonly EventStore.ClientAPI.ILogger ApiLogger =
		new ClientApiLoggerBridge(Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName,
			"client-api"));

	protected readonly UserCredentials AdminCredentials =
		new UserCredentials(SystemUsers.Admin, SystemUsers.DefaultAdminPassword);

	protected readonly Action<IPEndPoint, byte[]> DirectSendOverTcp;
	protected readonly int MaxConcurrentRequests;
	protected readonly int Connections;
	protected readonly int Streams;
	protected readonly int EventsPerStream;
	protected readonly int StreamDeleteStep;

	private readonly NodeConnectionInfo _customNodeConnection;
	private readonly NodeConnectionInfo _nodeConnection;

	private readonly HashSet<int> _startedNodesProcIds;

	private readonly string _dbPath;

	private readonly Dictionary<WriteMode, Func<string, int, Func<int, EventData>, Task>> _writeHandlers;
	private readonly IEventStoreConnection[] _connections;
	private int _nextConnectionNum = -1;
	private readonly ProjectionsManager _projectionsManager;

	protected virtual TimeSpan StartupWaitInterval {
		get { return TimeSpan.FromSeconds(12); }
	}

	protected ScenarioBase(Action<IPEndPoint, byte[]> directSendOverTcp,
		int maxConcurrentRequests,
		int connections,
		int streams,
		int eventsPerStream,
		int streamDeleteStep,
		string dbParentPath,
		NodeConnectionInfo customNodeConnection) {
		DirectSendOverTcp = directSendOverTcp;
		MaxConcurrentRequests = maxConcurrentRequests;
		Connections = connections;
		Streams = streams;
		EventsPerStream = eventsPerStream;
		StreamDeleteStep = streamDeleteStep;
		_customNodeConnection = customNodeConnection;

		_startedNodesProcIds = new HashSet<int>();

		var ipAddress = IPAddress.Loopback;

		if (_customNodeConnection != null) {
			_nodeConnection = _customNodeConnection;
			_dbPath = null;
		} else {
			_dbPath = CreateNewDbPath(dbParentPath);
			_nodeConnection = new NodeConnectionInfo(ipAddress,
				PortsHelper.GetAvailablePort(ipAddress),
				PortsHelper.GetAvailablePort(ipAddress));
		}

		_connections = new IEventStoreConnection[connections];

		Log.Information("Projection manager points to {nodeConnection}.", _nodeConnection);
		_projectionsManager = new ProjectionsManager(new ConsoleLogger(),
			new IPEndPoint(_nodeConnection.IpAddress, _nodeConnection.HttpPort), TimeSpan.FromMilliseconds(5000));

		_writeHandlers = new Dictionary<WriteMode, Func<string, int, Func<int, EventData>, Task>> {
			{WriteMode.SingleEventAtTime, WriteSingleEventAtTime},
			{WriteMode.Bucket, WriteBucketOfEventsAtTime},
			{WriteMode.Transactional, WriteEventsInTransactionalWay}
		};
	}

	private static string CreateNewDbPath(string dbParentPath) {
		var dbParent = dbParentPath ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

		var dataFolder = Path.Combine(dbParent, "data");
		var idx = 0;
		var dbPath = Path.Combine(dataFolder, string.Format("es_{0}", idx));

		while (Directory.Exists(dbPath)) {
			idx += 1;
			dbPath = Path.Combine(dataFolder, string.Format("es_{0}", idx));
		}

		return dbPath;
	}

	protected IEventStoreConnection GetConnection() {
		var connectionNum = (int)(((uint)Interlocked.Increment(ref _nextConnectionNum)) % Connections);
		return _connections[connectionNum];
	}

	protected ProjectionsManager GetProjectionsManager() {
		return _projectionsManager;
	}

	public void Run() {
		const int maxReconnections = 200;
		const int maxOperationRetries = 200;

		for (int i = 0; i < Connections; ++i) {
			_connections[i] = EventStoreConnection.Create(
				ConnectionSettings.Create()
					.UseCustomLogger(ApiLogger)
					.LimitConcurrentOperationsTo(MaxConcurrentRequests)
					.LimitRetriesForOperationTo(maxReconnections)
					.LimitReconnectionsTo(maxOperationRetries)
					.FailOnNoServerResponse(),
				new Uri(string.Format("tcp://admin:changeit@{0}:{1}", _nodeConnection.IpAddress,
					_nodeConnection.TcpPort)),
				string.Format("ESConn-{0}", i));
			_connections[i].Closed += (s, e) =>
				Log.Debug("[SCENARIO] {connection} closed.", e.Connection.ConnectionName);
			_connections[i].Connected += (s, e) =>
				Log.Debug("[SCENARIO] {connection} connected to [{remoteEndPoint}].", e.Connection.ConnectionName,
					e.RemoteEndPoint);
			_connections[i].Disconnected += (s, e) =>
				Log.Debug("[SCENARIO] {connection} disconnected from [{remoteEndPoint}].",
					e.Connection.ConnectionName, e.RemoteEndPoint);
			_connections[i].Reconnecting += (s, e) =>
				Log.Debug("[SCENARIO] {connection} reconnecting.", e.Connection.ConnectionName);
			_connections[i].ErrorOccurred += (s, e) => Log.Debug(e.Exception,
				"[SCENARIO] {connection} error occurred.", e.Connection.ConnectionName);
			_connections[i].ConnectAsync().Wait();
		}

		RunInternal();
	}

	protected abstract void RunInternal();

	public void Clean() {
		CloseConnections();
		KillStartedNodes();
		DeleteDatabase();
	}

	private void DeleteDatabase() {
		try {
			if (_dbPath != null) {
				Log.Information("Deleting {dbPath}...", _dbPath);
				Directory.Delete(_dbPath, true);
				Log.Information("Deleted {dbPath}", _dbPath);
			}
		} catch (IOException ex) {
			Log.Error(ex, "Failed to delete dir {dbPath}, IOException was raised", _dbPath);
		} catch (UnauthorizedAccessException ex) {
			Log.Error(ex, "Failed to delete dir {dbPath}, UnauthorizedAccessException was raised",
				_dbPath);
		}
	}

	protected T[][] Split<T>(IEnumerable<T> sequence, int parts) {
		return sequence.Select((x, i) => new {GroupNum = i % parts, Item = x})
			.GroupBy(x => x.GroupNum, y => y.Item)
			.Select(x => x.ToArray())
			.ToArray();
	}


	protected Task Write(WriteMode mode, string[] streams, int eventsPerStream) {
		Func<int, EventData> createEvent = TestEvent.NewTestEvent;
		return Write(mode, streams, eventsPerStream, createEvent);
	}

	protected Task Write(WriteMode mode, string[] streams, int eventsPerStream, Func<int, EventData> createEvent) {
		Log.Information(
			"Writing. Mode : {mode,-15} Streams : {streamsLength,-10} Events per stream : {eventsPerStream,-10}",
			mode,
			streams.Length,
			eventsPerStream);

		Func<string, int, Func<int, EventData>, Task> handler;
		if (!_writeHandlers.TryGetValue(mode, out handler))
			throw new ArgumentOutOfRangeException("mode");

		var tasks = new List<Task>();
		for (var i = 0; i < streams.Length; i++) {
			//Console.WriteLine("WRITING TO {0}", streams[i]);
			tasks.Add(handler(streams[i], eventsPerStream, createEvent));
		}

		return Task.Factory.ContinueWhenAll(tasks.ToArray(), tsks => {
			Task.WaitAll(tsks);
			Log.Information(
				"Finished writing. Mode : {mode,-15} Streams : {streamsLength,-10} Events per stream : {eventsPerStream,-10}",
				mode,
				streams.Length,
				eventsPerStream);
		});
	}

	protected void DeleteStreams(IEnumerable<string> streams) {
		Log.Information("Deleting streams...");
		var store = GetConnection();

		var tasks = new List<Task>();
		foreach (var stream in streams) {
			var s = stream;
			Log.Information("Deleting stream {stream}...", stream);
			var task = store.DeleteStreamAsync(stream, (EventsPerStream - 1), hardDelete: true)
				.ContinueWith(x => Log.Information("Stream {stream} successfully deleted", s));

			tasks.Add(task);
		}

		Task.WaitAll(tasks.ToArray());

		Log.Information("All streams successfully deleted");
	}

	protected Task CheckStreamsDeleted(IEnumerable<string> streams) {
		Log.Information("Verifying streams are deleted...");

		var store = GetConnection();
		var tasks = new List<Task>();

		foreach (var stream in streams) {
			var s = stream;
			var task = store.ReadStreamEventsForwardAsync(stream, 0, 1, resolveLinkTos: false).ContinueWith(t => {
				if (t.Result.Status != SliceReadStatus.StreamDeleted)
					throw new Exception(string.Format("Stream '{0}' is not deleted, but should be!", s));
			});

			tasks.Add(task);
		}

		return Task.Factory.ContinueWhenAll(tasks.ToArray(), tsks => {
			Task.WaitAll(tsks);
			Log.Information("Stream deletion verification succeeded.");
		});
	}

	protected Task Read(string[] streams, int @from, int count) {
		if (streams.Length == 0) {
			Debugger.Break();
			throw new Exception("Streams shouldn't be empty.");
		}

		Log.Information("Reading [{streams}]\nfrom {from,-10} count {count,-10}", string.Join(",", streams), from, count);

		var tasks = new List<Task>();

		for (int i = 0; i < streams.Length; i++) {
			var task = ReadStream(streams[i], from, count);
			tasks.Add(task);
		}

		return Task.Factory.ContinueWhenAll(tasks.ToArray(), tsks => {
			Task.WaitAll(tsks);
			Log.Information("Done reading [{streams}]", string.Join(",", streams));
		});
	}

	private bool TryGetPathToMono(out string pathToMono) {
		const string monopathVariable = "EVENTSTORE_MONOPATH";
		pathToMono = Environment.GetEnvironmentVariable(monopathVariable);
		return !string.IsNullOrEmpty(pathToMono);
	}

	protected int StartNode() {
		int processId = -1;
		if (_customNodeConnection == null)
			processId = StartNewNode();

		return processId;
	}

	private int StartNewNode() {
		var clientFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		//../../KurrentDB/net471/KurrentDB.exe
		var clusterNodeFolder = System.IO.Directory.GetParent(System.IO.Directory.GetParent(clientFolder).FullName)
		                        + "/KurrentDB/net471/";
		string fileName;
		string argumentsHead;

		string pathToMono;
		if (TryGetPathToMono(out pathToMono)) {
			Log.Information("Mono at {pathToMono} will be used.", pathToMono);
			fileName = pathToMono;
			argumentsHead = string.Format("--debug --gc=sgen {0}",
				Path.Combine(clusterNodeFolder, "KurrentDB.exe"));
		} else {
			fileName = Path.Combine(clusterNodeFolder, "KurrentDB.exe");
			argumentsHead = "";
		}

		var arguments = string.Format(
			"{0} --run-projections=all --ext-ip {1} --ext-tcp-port {2} --ext-http-port {3} --db {4}",
			argumentsHead,
			_nodeConnection.IpAddress,
			_nodeConnection.TcpPort,
			_nodeConnection.HttpPort,
			_dbPath);

		Log.Information("Starting [{fileName} {arguments}]...", fileName, arguments);

		var startInfo = new ProcessStartInfo(fileName, arguments);

		var nodeProcess = Process.Start(startInfo);
		if (nodeProcess == null || nodeProcess.HasExited)
			throw new ApplicationException(string.Format("Process was not started [{0} {1}].", fileName,
				arguments));

		Thread.Sleep(3000);
		Process tmp;
		var running = TryGetProcessById(nodeProcess.Id, out tmp);
		if (!running || tmp.HasExited)
			throw new ApplicationException(string.Format("Process was not started [{0} {1}].", fileName,
				arguments));

		_startedNodesProcIds.Add(nodeProcess.Id);

		Log.Information("Started node with process id {id}", nodeProcess.Id);

		Thread.Sleep(StartupWaitInterval);
		Log.Information("Started [{fileName} {arguments}]", fileName, arguments);

		return nodeProcess.Id;
	}

	private bool TryGetProcessById(int processId, out Process process) {
		process = null;

		try {
			process = Process.GetProcessById(processId);
		} catch (ArgumentException) {
			return false;
		} catch (InvalidOperationException) {
			return false;
		}

		return true;
	}

	protected void KillNode(int processId) {
		if (processId != -1)
			KillStartedNode(processId);
		else
			Log.Information("Skip killing, procId -1");
	}

	private void KillStartedNode(int processId) {
		Log.Information("Killing {processId}...", processId);

		Process process;
		if (TryGetProcessById(processId, out process)) {
			process.Kill();

			var waitCount = 200;
			while (!process.HasExited && waitCount > 0) {
				Thread.Sleep(250);
				waitCount -= 1;
			}

			if (process.HasExited) {
				_startedNodesProcIds.Remove(processId);

				PortsHelper.ReturnPort(_nodeConnection.TcpPort);
				PortsHelper.ReturnPort(_nodeConnection.HttpPort);

				Log.Information("Killed process {processId}, wait a bit.", processId);
				Thread.Sleep(1000); // wait for system to release port used by HttpListener.
			} else {
				Process temp;
				if (TryGetProcessById(processId, out temp))
					Log.Error(
						"Process {processId} did not report about exit in time and is still present in processes list.",
						processId);
				else
					Log.Information("Process {processId} did not report about exit in time but is not found again.",
						processId);
			}
		} else
			Log.Error("Process {processId} was not found to be killed.", processId);
	}

	public void Dispose() {
		CloseConnections();
		Thread.Sleep(2 * 1000);
		KillStartedNodes();
	}

	private void CloseConnections() {
		for (int i = 0; i < _connections.Length; ++i) {
			_connections[i].Close();
		}
	}

	private void KillStartedNodes() {
		Log.Information("Killing remaining nodes...");
		try {
			_startedNodesProcIds.ToList().ForEach(KillNode);
		} catch (Exception ex) {
			Log.Error("Failed to kill started nodes: {e}.", ex.Message);
		}
	}

	protected void Scavenge() {
		Log.Information("Send scavenge command...");
		var package = new TcpPackage(TcpCommand.ScavengeDatabase, Guid.NewGuid(), null).AsByteArray();
		DirectSendOverTcp(new IPEndPoint(_nodeConnection.IpAddress, _nodeConnection.TcpPort), package);
		Log.Information("Scavenge command was sent.");
	}

	private Task WriteSingleEventAtTime(string stream, int events, Func<int, EventData> createEvent) {
		var resSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

		Log.Information("Starting to write {events} events to [{stream}]", events, stream);
		var store = GetConnection();
		int eventVersion = 0;

		Action<Task> fail = prevTask => {
			Log.Information("WriteSingleEventAtTime for stream {stream} failed.", stream);
			resSource.SetException(prevTask.Exception);
		};

		Action<Task> writeSingleEvent = null;
		writeSingleEvent = _ => {
			if (eventVersion == events) {
				Log.Information("Wrote {events} events to [{stream}]", events, stream);
				resSource.SetResult(null);
				return;
			}

			var writeTask = store.AppendToStreamAsync(stream,
				eventVersion - 1,
				new[] {createEvent(eventVersion)});

			eventVersion += 1;

			writeTask.ContinueWith(fail, TaskContinuationOptions.OnlyOnFaulted);
			writeTask.ContinueWith(writeSingleEvent, TaskContinuationOptions.OnlyOnRanToCompletion);
		};

		writeSingleEvent(null);

		return resSource.Task;
	}

	private Task WriteBucketOfEventsAtTime(string stream, int eventCount, Func<int, EventData> createEvent) {
		const int bucketSize = 25;
		Log.Information("Starting to write {eventCount} events to [{stream}] ({bucketSize} events at once)", eventCount,
			stream, bucketSize);

		var resSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		var store = GetConnection();
		int writtenCount = 0;

		Action<Task> fail = prevTask => {
			Log.Information("WriteBucketOfEventsAtTime for stream {stream} failed.", stream);
			resSource.SetException(prevTask.Exception);
		};

		Action<Task> writeBatch = null;
		writeBatch = _ => {
			if (writtenCount == eventCount) {
				Log.Information("Wrote {eventCount} events to [{stream}] ({bucketSize} events at once)", eventCount,
					stream, bucketSize);
				resSource.SetResult(null);
				return;
			}

			var startIndex = writtenCount;
			var endIndex = Math.Min(eventCount, startIndex + bucketSize);
			var events = Enumerable.Range(startIndex, endIndex - startIndex).Select(createEvent).ToArray();

			writtenCount = endIndex;

			var expectedVersion = startIndex - 1;
			var writeTask = store.AppendToStreamAsync(stream, expectedVersion, events);

			writeTask.ContinueWith(fail, TaskContinuationOptions.OnlyOnFaulted);
			writeTask.ContinueWith(writeBatch, TaskContinuationOptions.OnlyOnRanToCompletion);
		};

		writeBatch(null);

		return resSource.Task;
	}

	private Task WriteEventsInTransactionalWay(string stream, int eventCount, Func<int, EventData> createEvent) {
		Log.Information("Starting to write {eventCount} events to [{stream}] (in single transaction)", eventCount, stream);

		var resSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		var store = GetConnection();

		Action<Task> fail = prevTask => {
			Log.Information("WriteEventsInTransactionalWay for stream {stream} failed.", stream);
			resSource.SetException(prevTask.Exception);
		};

		int writtenCount = 0;
		EventStoreTransaction transaction = null;

		Action<Task> writeTransactionEvent = null;
		writeTransactionEvent = prevTask => {
			if (writtenCount == eventCount) {
				var commitTask = transaction.CommitAsync();
				commitTask.ContinueWith(fail, TaskContinuationOptions.OnlyOnFaulted);
				commitTask.ContinueWith(t => {
					Log.Information("Wrote {eventCount} events to [{stream}] (in single transaction)", eventCount, stream);
					resSource.SetResult(null);
				}, TaskContinuationOptions.OnlyOnRanToCompletion);
				return;
			}

			var writeTask = transaction.WriteAsync(createEvent(writtenCount));

			writtenCount += 1;

			writeTask.ContinueWith(fail, TaskContinuationOptions.OnlyOnFaulted);
			writeTask.ContinueWith(writeTransactionEvent, TaskContinuationOptions.OnlyOnRanToCompletion);
		};

		var startTask = store.StartTransactionAsync(stream, -1);
		startTask.ContinueWith(fail, TaskContinuationOptions.OnlyOnFaulted);
		startTask.ContinueWith(t => {
			transaction = t.Result;
			writeTransactionEvent(t);
		}, TaskContinuationOptions.OnlyOnRanToCompletion);

		return resSource.Task;
	}

	private Task ReadStream(string stream, int from, int count) {
		Log.Information("Reading [{stream}] from {from,-10} count {count,-10}", stream, from, count);
		var resSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		var store = GetConnection();

		Action<Task> fail = prevTask => {
			Log.Information("ReadStream for stream {stream} failed.", stream);
			resSource.SetException(prevTask.Exception);
		};

		var readTask = store.ReadStreamEventsForwardAsync(stream, @from, count, resolveLinkTos: false);
		readTask.ContinueWith(fail, TaskContinuationOptions.OnlyOnFaulted);
		readTask.ContinueWith(t => {
			try {
				var slice = t.Result;
				if (slice?.Events == null || slice.Events.Length != count) {
					throw new Exception(string.Format(
						"Tried to read {0} events from event number {1} from stream '{2}' but failed. Reason: {3}.",
						count,
						from,
						stream,
						slice == null ? "slice == null"
						: slice.Events == null ? "slive.Events == null"
						: slice.Events.Length != count ? string.Format("Expected count: {0}, actual count: {1}.",
							count, slice.Events.Length)
						: "WAT?!?"));
				}

				for (int i = 0; i < count; ++i) {
					var evnt = slice.Events[i].Event;
					if (evnt.EventNumber != i + from) {
						throw new Exception(string.Format(
							"Received event with wrong event number. Expected: {0}, actual: {1}.\nEvent: {2}.",
							from + i,
							evnt.EventNumber,
							evnt));
					}

					TestEvent.VerifyIfMatched(evnt);
				}

				Log.Information("Done reading [{stream}] from {from,-10} count {count,-10}", stream, from, count);
				resSource.SetResult(null);
			} catch (Exception exc) {
				Log.Information("ReadStream for stream {stream} failed.", stream);
				resSource.SetException(exc);
			}
		}, TaskContinuationOptions.OnlyOnRanToCompletion);

		return resSource.Task;
	}
}
