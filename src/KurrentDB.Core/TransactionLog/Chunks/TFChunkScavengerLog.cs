// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.TransactionLog.Chunks;

class TFChunkScavengerLog : ITFChunkScavengerLog {
	private readonly object _updateLock = new object();

	private readonly string _streamName;
	private readonly IODispatcher _ioDispatcher;
	private readonly string _scavengeId;
	private readonly string _nodeId;
	private readonly int _retryAttempts;
	private readonly TimeSpan _scavengeHistoryMaxAge;
	private static readonly ILogger Log = Serilog.Log.ForContext<StorageScavenger>();
	private long _spaceSaved;

	/// <summary>
	/// When we're scavenging, we want to keep track of the chunk high watermark (this won't be perfect in multi-threaded scavenges)
	/// as it will give us a good approximate place to resume the scavenge if it gets interrupted.
	/// </summary>
	private int _maxChunkScavenged;

	public TFChunkScavengerLog(IODispatcher ioDispatcher, string scavengeId, string nodeId, int retryAttempts,
		TimeSpan scavengeHistoryMaxAge) {
		_ioDispatcher = ioDispatcher;
		_scavengeId = scavengeId;
		_nodeId = nodeId;
		_retryAttempts = retryAttempts;
		_scavengeHistoryMaxAge = scavengeHistoryMaxAge;

		_streamName = string.Format("{0}-{1}", SystemStreams.ScavengesStream, scavengeId);
	}

	public string ScavengeId => _scavengeId;

	public long SpaceSaved => Interlocked.Read(ref _spaceSaved);

	public void ScavengeStarted() {
		ScavengeStartedInternal(new Dictionary<string, object> {
			{"scavengeId", _scavengeId},
			{"nodeEndpoint", _nodeId},
		});
	}

	public void ScavengeStarted(bool alwaysKeepScavenged, bool mergeChunks, int startFromChunk, int threads) {
		ScavengeStartedInternal(new Dictionary<string, object> {
			{"scavengeId", _scavengeId},
			{"nodeEndpoint", _nodeId},
			{"alwaysKeepScavenged", alwaysKeepScavenged},
			{"mergeChunks", mergeChunks},
			{"startFromChunk", startFromChunk},
			{"threads", threads},
		});
	}

	private void ScavengeStartedInternal(Dictionary<string, object> payload) {
		var metadataEventId = Guid.NewGuid();
		var metaStreamId = SystemStreams.MetastreamOf(_streamName);
		var acl = new StreamAcl(
			new string[] { "$ops" },
			new string[] { },
			new string[] { },
			new string[] { },
			new string[] { }
		);
		var metadata = new StreamMetadata(maxAge: _scavengeHistoryMaxAge, acl: acl);
		var metaStreamEvent = new Event(metadataEventId, SystemEventTypes.StreamMetadata, isJson: true,
			data: metadata.ToJsonBytes(), metadata: null, SchemaInfo.None, SchemaInfo.None);
		_ioDispatcher.WriteEvent(metaStreamId, ExpectedVersion.Any, metaStreamEvent, SystemAccounts.System, m => {
			if (m.Result != OperationResult.Success) {
				Log.Error(
					"Failed to write the $maxAge of {days} days metadata for the {stream} stream. Reason: {reason}",
					_scavengeHistoryMaxAge.TotalDays, _streamName, m.Result);
			}
		});

		var scavengeStartedEvent = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeStarted, true,
			payload.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);
		WriteScavengeDetailEvent(_streamName, scavengeStartedEvent, _retryAttempts);
	}

	public void ScavengeCompleted(ScavengeResult result, string error, TimeSpan elapsed)
		=> ScavengeCompleted(result, error, elapsed, _spaceSaved, _maxChunkScavenged);

	internal void ScavengeCompleted(ScavengeResult result, string error, TimeSpan elapsed, long spaceSaved, int maxChunkScavenged) {
		var scavengeCompletedEvent = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeCompleted, true,
			new Dictionary<string, object> {
				{"scavengeId", _scavengeId},
				{"nodeEndpoint", _nodeId},
				{"result", result},
				{"error", error},
				{"timeTaken", elapsed},
				{"spaceSaved", spaceSaved},
				{"maxChunkScavenged", maxChunkScavenged}
			}.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);
		WriteScavengeDetailEvent(_streamName, scavengeCompletedEvent, _retryAttempts);
	}

	public void ChunksScavenged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, long spaceSaved) {
		Interlocked.Add(ref _spaceSaved, spaceSaved);
		lock (_updateLock)
			_maxChunkScavenged = Math.Max(_maxChunkScavenged, chunkEndNumber);
		var evnt = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeChunksCompleted, true,
			new Dictionary<string, object> {
				{"scavengeId", _scavengeId},
				{"chunkStartNumber", chunkStartNumber},
				{"chunkEndNumber", chunkEndNumber},
				{"timeTaken", elapsed},
				{"wasScavenged", true},
				{"spaceSaved", spaceSaved},
				{"nodeEndpoint", _nodeId},
				{"errorMessage", ""}
			}.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);

		WriteScavengeChunkCompletedEvent(_streamName, evnt, _retryAttempts);
	}

	public void ChunksNotScavenged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed,
		string errorMessage) {
		// We still update the _maxChunkScavenged as we've processed it during our scavenge stage.
		lock (_updateLock)
			_maxChunkScavenged = Math.Max(_maxChunkScavenged, chunkEndNumber);
		var evnt = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeChunksCompleted, true,
			new Dictionary<string, object> {
				{"scavengeId", _scavengeId},
				{"chunkStartNumber", chunkStartNumber},
				{"chunkEndNumber", chunkEndNumber},
				{"timeTaken", elapsed},
				{"wasScavenged", false},
				{"spaceSaved", 0},
				{"nodeEndpoint", _nodeId},
				{"errorMessage", errorMessage}
			}.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);

		WriteScavengeChunkCompletedEvent(_streamName, evnt, _retryAttempts);
	}

	public void ChunksMerged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, long spaceSaved) {
		Interlocked.Add(ref _spaceSaved, spaceSaved);
		var evnt = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeMergeCompleted, true,
			new Dictionary<string, object> {
				{"scavengeId", _scavengeId},
				{"chunkStartNumber", chunkStartNumber},
				{"chunkEndNumber", chunkEndNumber},
				{"timeTaken", elapsed},
				{"spaceSaved", spaceSaved},
				{"wasMerged", true},
				{"nodeEndpoint", _nodeId},
				{"errorMessage", ""}
			}.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);

		WriteScavengeChunkCompletedEvent(_streamName, evnt, _retryAttempts);
	}

	public void ChunksNotMerged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, string errorMessage) {
		var evnt = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeMergeCompleted, true,
			new Dictionary<string, object> {
				{"scavengeId", _scavengeId},
				{"chunkStartNumber", chunkStartNumber},
				{"chunkEndNumber", chunkEndNumber},
				{"timeTaken", elapsed},
				{"spaceSaved", 0},
				{"wasMerged", false},
				{"nodeEndpoint", _nodeId},
				{"errorMessage", errorMessage}
			}.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);

		WriteScavengeChunkCompletedEvent(_streamName, evnt, _retryAttempts);
	}


	public void IndexTableScavenged(int level, int index, TimeSpan elapsed, long entriesDeleted, long entriesKept,
		long spaceSaved) {
		Interlocked.Add(ref _spaceSaved, spaceSaved);
		var evnt = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeIndexCompleted, true,
			new Dictionary<string, object> {
				{"scavengeId", _scavengeId},
				{"level", level},
				{"index", index},
				{"timeTaken", elapsed},
				{"entriesDeleted", entriesDeleted},
				{"entriesKept", entriesKept},
				{"spaceSaved", spaceSaved},
				{"wasScavenged", true},
				{"nodeEndpoint", _nodeId},
				{"errorMessage", ""}
			}.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);

		WriteScavengeChunkCompletedEvent(_streamName, evnt, _retryAttempts);
	}

	public void IndexTableNotScavenged(int level, int index, TimeSpan elapsed, long entriesKept,
		string errorMessage) {
		var evnt = new Event(Guid.NewGuid(), SystemEventTypes.ScavengeIndexCompleted, true,
			new Dictionary<string, object> {
				{"scavengeId", _scavengeId},
				{"level", level},
				{"index", index},
				{"timeTaken", elapsed},
				{"entriesDeleted", 0},
				{"entriesKept", entriesKept},
				{"spaceSaved", 0},
				{"wasScavenged", false},
				{"nodeEndpoint", _nodeId},
				{"errorMessage", errorMessage}
			}.ToJsonBytes(), null, SchemaInfo.None, SchemaInfo.None);

		WriteScavengeChunkCompletedEvent(_streamName, evnt, _retryAttempts);
	}

	private void WriteScavengeChunkCompletedEvent(string streamId, Event eventToWrite, int retryCount) {
		_ioDispatcher.WriteEvent(streamId, ExpectedVersion.Any, eventToWrite, SystemAccounts.System,
			m => WriteScavengeChunkCompletedEventCompleted(m, streamId, eventToWrite, retryCount));
	}

	private void WriteScavengeChunkCompletedEventCompleted(ClientMessage.WriteEventsCompleted msg, string streamId,
		Event eventToWrite, int retryCount) {
		if (msg.Result != OperationResult.Success) {
			if (retryCount > 0) {
				WriteScavengeChunkCompletedEvent(streamId, eventToWrite, --retryCount);
			} else {
				Log.Error(
					"Failed to write an event to the {stream} stream. Retry limit of {retryCount} reached. Reason: {reason}",
					streamId, _retryAttempts, msg.Result);
			}
		}
	}

	private void WriteScavengeDetailEvent(string streamId, Event eventToWrite, int retryCount) {
		_ioDispatcher.WriteEvent(streamId, ExpectedVersion.Any, eventToWrite, SystemAccounts.System,
			x => WriteScavengeDetailEventCompleted(x, eventToWrite, streamId, retryCount));
	}

	private void WriteScavengeIndexEvent(Event linkToEvent, int retryCount) {
		_ioDispatcher.WriteEvent(SystemStreams.ScavengesStream, ExpectedVersion.Any, linkToEvent,
			SystemAccounts.System, m => WriteScavengeIndexEventCompleted(m, linkToEvent, retryCount));
	}

	private void WriteScavengeIndexEventCompleted(ClientMessage.WriteEventsCompleted msg, Event linkToEvent,
		int retryCount) {
		if (msg.Result != OperationResult.Success) {
			if (retryCount > 0) {
				Log.Error(
					"Failed to write an event to the {stream} stream. Retrying {retry}/{retryCount}. Reason: {reason}",
					SystemStreams.ScavengesStream, (_retryAttempts - retryCount) + 1, _retryAttempts, msg.Result);
				WriteScavengeIndexEvent(linkToEvent, --retryCount);
			} else {
				Log.Error(
					"Failed to write an event to the {stream} stream. Retry limit of {retryCount} reached. Reason: {reason}",
					SystemStreams.ScavengesStream, _retryAttempts, msg.Result);
			}
		}
	}

	private void WriteScavengeDetailEventCompleted(ClientMessage.WriteEventsCompleted msg, Event eventToWrite,
		string streamId, int retryCount) {
		if (msg.Result != OperationResult.Success) {
			if (retryCount > 0) {
				Log.Error(
					"Failed to write an event to the {stream} stream. Retrying {retry}/{retryCount}. Reason: {reason}",
					streamId, (_retryAttempts - retryCount) + 1, _retryAttempts, msg.Result);
				WriteScavengeDetailEvent(streamId, eventToWrite, --retryCount);
			} else {
				Log.Error(
					"Failed to write an event to the {stream} stream. Retry limit of {retryCount} reached. Reason: {reason}",
					streamId, _retryAttempts, msg.Result);
			}
		} else {
			string eventLinkTo = string.Format("{0}@{1}", msg.FirstEventNumber, streamId);
			var linkToIndexEvent = new Event(Guid.NewGuid(), SystemEventTypes.LinkTo, false, eventLinkTo, null, SchemaInfo.None, SchemaInfo.None);
			WriteScavengeIndexEvent(linkToIndexEvent, _retryAttempts);
		}
	}
}
