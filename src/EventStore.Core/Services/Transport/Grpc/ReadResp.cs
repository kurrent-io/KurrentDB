// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
namespace EventStore.Client.Streams;

// ReSharper restore CheckNamespace
partial class ReadResp {
	partial class Types {
		partial class ReadEvent {
			public Types.RecordedEvent OriginalEvent => Link ?? Event;
		}
	}
}
