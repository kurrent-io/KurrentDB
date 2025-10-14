// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Client.Streams;
using EventStore.Plugins.Authorization;
using KurrentDB.Common.Configuration;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Storage.ReaderIndex;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Streams<TStreamId> : Streams.StreamsBase {
	private readonly IPublisher _publisher;
	private readonly int _maxAppendSize;
	private readonly int _maxAppendEventSize;
	private readonly TimeSpan _writeTimeout;
	private readonly IExpiryStrategy _expiryStrategy;
	private readonly IDurationTracker _readTracker;
	private readonly IDurationTracker _appendTracker;
	private readonly IDurationTracker _batchAppendTracker;
	private readonly IDurationTracker _deleteTracker;
	private readonly IDurationTracker _tombstoneTracker;
	private readonly IAuthorizationProvider _provider;
	private static readonly Operation ReadOperation = new Operation(Plugins.Authorization.Operations.Streams.Read);
	private static readonly Operation WriteOperation = new Operation(Plugins.Authorization.Operations.Streams.Write);
	private static readonly Operation DeleteOperation = new Operation(Plugins.Authorization.Operations.Streams.Delete);

	public Streams(IPublisher publisher, int maxAppendSize, int maxAppendEventSize, TimeSpan writeTimeout,
		IExpiryStrategy expiryStrategy,
		GrpcTrackers trackers,
		IAuthorizationProvider provider) {

		if (publisher == null)
			throw new ArgumentNullException(nameof(publisher));
		_publisher = publisher;
		_maxAppendSize = maxAppendSize;
		_maxAppendEventSize = maxAppendEventSize;
		_writeTimeout = writeTimeout;
		_expiryStrategy = expiryStrategy;
		_readTracker = trackers[MetricsConfiguration.GrpcMethod.StreamRead];
		_appendTracker = trackers[MetricsConfiguration.GrpcMethod.StreamAppend];
		_batchAppendTracker = trackers[MetricsConfiguration.GrpcMethod.StreamBatchAppend];
		_deleteTracker = trackers[MetricsConfiguration.GrpcMethod.StreamDelete];
		_tombstoneTracker = trackers[MetricsConfiguration.GrpcMethod.StreamTombstone];
		_provider = provider;
	}
}
