// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public partial class EmittedStream {
	public class WriterConfiguration {
		public class StreamMetadata(int? maxCount = null, TimeSpan? maxAge = null) {
			public int? MaxCount { get; } = maxCount;
			public TimeSpan? MaxAge { get; } = maxAge;
		}

		public WriterConfiguration(IEmittedStreamsWriter writer, StreamMetadata streamMetadata, ClaimsPrincipal writeAs,
			int maxWriteBatchLength, ILogger logger = null) {
			Writer = writer;
			WriteAs = writeAs;
			MaxWriteBatchLength = maxWriteBatchLength;
			Logger = logger;
			if (streamMetadata != null) {
				MaxCount = streamMetadata.MaxCount;
				MaxAge = streamMetadata.MaxAge;
			}
		}

		public ClaimsPrincipal WriteAs { get; }
		public int MaxWriteBatchLength { get; }
		public ILogger Logger { get; }
		public int? MaxCount { get; }
		public TimeSpan? MaxAge { get; }
		public IEmittedStreamsWriter Writer { get; }
	}
}
