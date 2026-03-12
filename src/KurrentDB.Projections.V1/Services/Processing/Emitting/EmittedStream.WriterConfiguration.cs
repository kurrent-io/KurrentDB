// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public partial class EmittedStream {
	public class WriterConfiguration {
		private readonly ClaimsPrincipal _writeAs;
		private readonly int _maxWriteBatchLength;
		private readonly ILogger _logger;

		private readonly int? maxCount;
		private readonly TimeSpan? maxAge;

		private readonly IEmittedStreamsWriter _writer;

		// Keep StreamMetadata as a type alias for backwards compatibility
		public class StreamMetadata : EmittedStreamMetadata {
			public StreamMetadata(int? maxCount = null, TimeSpan? maxAge = null)
				: base(maxCount, maxAge) {
			}
		}

		public WriterConfiguration(
			IEmittedStreamsWriter writer, EmittedStreamMetadata streamMetadata, ClaimsPrincipal writeAs,
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
