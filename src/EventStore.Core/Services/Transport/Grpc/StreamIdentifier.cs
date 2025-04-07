// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Text;
using Google.Protobuf;

// ReSharper disable once CheckNamespace
namespace EventStore.Client;

partial class StreamIdentifier {
	private string _cached;
	public static implicit operator string(StreamIdentifier source) {
		if (source._cached != null || source.StreamName.IsEmpty)
			return source._cached;
		var tmp = Encoding.UTF8.GetString(source.StreamName.Span);
		//this doesn't have to be thread safe, its just a cache in case the identifier is turned into a string several times
		source._cached = tmp;
		return source._cached;
	}

	public static implicit operator StreamIdentifier(string source) =>
		new() { StreamName = ByteString.CopyFromUtf8(source) };
}
