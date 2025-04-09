// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.UserManagement;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Core.Services.PersistentSubscription;

public class PersistentSubscriptionCheckpointWriter : IPersistentSubscriptionCheckpointWriter {
	private readonly IODispatcher _ioDispatcher;
	private long _version = ExpectedVersion.Any;
	private bool _outstandingWrite;
	private readonly string _subscriptionStateStream;
	private static readonly ILogger Log = Serilog.Log.ForContext<PersistentSubscriptionCheckpointWriter>();

	public PersistentSubscriptionCheckpointWriter(string subscriptionId, IODispatcher ioDispatcher) {
		_subscriptionStateStream = "$persistentsubscription-" + subscriptionId + "-checkpoint";
		_ioDispatcher = ioDispatcher;
	}

	public void StartFrom(long version) {
		_version = version;
	}

	public void BeginWriteState(IPersistentSubscriptionStreamPosition state) {
		if (_outstandingWrite) {
			return;
		}

		if (_version == ExpectedVersion.NoStream) {
			PublishMetadata(state);
		} else {
			PublishCheckpoint(state);
		}
	}

	public void BeginDelete(Action<IPersistentSubscriptionCheckpointWriter> completed) {
		_ioDispatcher.DeleteStream(_subscriptionStateStream, ExpectedVersion.Any, false, SystemAccounts.System,
			x => completed(this));
	}

	private void PublishCheckpoint(IPersistentSubscriptionStreamPosition state) {
		_outstandingWrite = true;
		var evnt = new Event(Guid.NewGuid(), "$SubscriptionCheckpoint", true, state.ToString().ToJson(), null, SchemaInfo.None, SchemaInfo.None);
		_ioDispatcher.WriteEvent(_subscriptionStateStream, _version, evnt, SystemAccounts.System,
			WriteStateCompleted);
	}

	private void PublishMetadata(IPersistentSubscriptionStreamPosition state) {
		_outstandingWrite = true;
		var metaStreamId = SystemStreams.MetastreamOf(_subscriptionStateStream);
		_ioDispatcher.WriteEvent(
			metaStreamId, ExpectedVersion.Any, CreateStreamMetadataEvent(), SystemAccounts.System, msg => {
				_outstandingWrite = false;
				switch (msg.Result) {
					case OperationResult.Success:
						PublishCheckpoint(state);
						break;
				}
			});
	}

	private Event CreateStreamMetadataEvent() {
		var eventId = Guid.NewGuid();
		var acl = new StreamAcl(
			readRole: SystemRoles.Admins, writeRole: SystemRoles.Admins,
			deleteRole: SystemRoles.Admins, metaReadRole: SystemRoles.All,
			metaWriteRole: SystemRoles.Admins);
		var metadata = new StreamMetadata(maxCount: 2, maxAge: null, cacheControl: null, acl: acl);
		var dataBytes = metadata.ToJsonBytes();
		return new Event(eventId, SystemEventTypes.StreamMetadata, isJson: true, data: dataBytes, metadata: null, SchemaInfo.None, SchemaInfo.None);
	}

	private void WriteStateCompleted(ClientMessage.WriteEventsCompleted msg) {
		_outstandingWrite = false;
		if (msg.Result == OperationResult.Success) {
			_version = msg.LastEventNumber;
		} else {
			Log.Debug("Error writing checkpoint for {stream}: {e}", _subscriptionStateStream, msg.Result);
			_version = ExpectedVersion.Any;
		}
	}
}
