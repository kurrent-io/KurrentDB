using System;
using EventStore.Common.Configuration;
using EventStore.Core.Bus;
using EventStore.Core.Metrics;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Plugins.Authorization;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class Streams<TStreamId> : EventStore.Client.Streams.Streams.StreamsBase {
		private readonly IPublisher _publisher;
		private readonly IReadIndex<TStreamId> _readIndex;
		private readonly int _maxAppendSize;
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

		public Streams(IPublisher publisher, IReadIndex<TStreamId> readIndex, int maxAppendSize, TimeSpan writeTimeout,
			IExpiryStrategy expiryStrategy,
			GrpcTrackers trackers,
			IAuthorizationProvider provider) {

			if (publisher == null)
				throw new ArgumentNullException(nameof(publisher));
			_publisher = publisher;
			_readIndex = readIndex;
			_maxAppendSize = maxAppendSize;
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
}
