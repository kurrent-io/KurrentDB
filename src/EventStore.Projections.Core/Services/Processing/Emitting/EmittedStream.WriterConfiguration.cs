// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using Serilog;

namespace EventStore.Projections.Core.Services.Processing.Emitting;

public partial class EmittedStream
{
	public class WriterConfiguration {
		private readonly ClaimsPrincipal _writeAs;
		private readonly int _maxWriteBatchLength;
		private readonly ILogger _logger;

		private readonly int? maxCount;
		private readonly TimeSpan? maxAge;

		private readonly IEmittedStreamsWriter _writer;

		public class StreamMetadata {
			private readonly int? _maxCount;
			private readonly TimeSpan? _maxAge;

			public StreamMetadata(int? maxCount = null, TimeSpan? maxAge = null) {
				_maxCount = maxCount;
				_maxAge = maxAge;
			}

			public int? MaxCount {
				get { return _maxCount; }
			}

			public TimeSpan? MaxAge {
				get { return _maxAge; }
			}
		}

		public WriterConfiguration(
			IEmittedStreamsWriter writer, StreamMetadata streamMetadata, ClaimsPrincipal writeAs,
			int maxWriteBatchLength, ILogger logger = null) {
			_writer = writer;
			_writeAs = writeAs;
			_maxWriteBatchLength = maxWriteBatchLength;
			_logger = logger;
			if (streamMetadata != null) {
				this.maxCount = streamMetadata.MaxCount;
				this.maxAge = streamMetadata.MaxAge;
			}
		}

		public ClaimsPrincipal WriteAs {
			get { return _writeAs; }
		}

		public int MaxWriteBatchLength {
			get { return _maxWriteBatchLength; }
		}

		public ILogger Logger {
			get { return _logger; }
		}

		public int? MaxCount {
			get { return maxCount; }
		}

		public TimeSpan? MaxAge {
			get { return maxAge; }
		}

		public IEmittedStreamsWriter Writer {
			get { return _writer; }
		}
	}
}
