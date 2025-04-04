// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;

namespace EventStore.Core.TransactionLog.Scavenging;

// Refers to a stream by name or by hash
// This struct is json serialized, don't change the names naively
// todo: consider making this not stream specific
public struct StreamHandle {
	public enum Kind : byte {
		None,
		Hash,
		Id,
	};

	public static StreamHandle<TStreamId> ForHash<TStreamId>(ulong streamHash) {
		return new StreamHandle<TStreamId>(kind: Kind.Hash, default, streamHash);
	}

	public static StreamHandle<TStreamId> ForStreamId<TStreamId>(TStreamId streamId) {
		return new StreamHandle<TStreamId>(kind: Kind.Id, streamId, default);
	}
}

// Refers to a stream by name or by hash
// this unifies the entries, some just have the hash (when we know they do not collide)
// some contain the full stream id (when they do collide)
public readonly struct StreamHandle<TStreamId> : IEquatable<StreamHandle<TStreamId>> {
	private static EqualityComparer<TStreamId> StreamIdComparer { get; } = EqualityComparer<TStreamId>.Default;

	public readonly StreamHandle.Kind Kind;
	public readonly TStreamId StreamId;
	public readonly ulong StreamHash;

	public StreamHandle(StreamHandle.Kind kind, TStreamId streamId, ulong streamHash) {
		Kind = kind;
		StreamId = streamId;
		StreamHash = streamHash;
	}

	public override string ToString() {
		switch (Kind) {
			case StreamHandle.Kind.Hash:
				return $"Hash: {StreamHash:N0}";
			case StreamHandle.Kind.Id:
				return $"Id: {StreamId}";
			case StreamHandle.Kind.None:
			default:
				return $"None";
		}
	}

	public override bool Equals(object other) {
		if (other == null)
			return false;
		if (!(other is StreamHandle<TStreamId> x))
			return false;
		return Equals(x);
	}

	public bool Equals(StreamHandle<TStreamId> other) =>
		Kind == other.Kind &&
		StreamIdComparer.Equals(StreamId, other.StreamId) &&
		StreamHash == other.StreamHash;

	// avoid the default, reflection based, implementations if we ever need to call these
	public override int GetHashCode() => throw new NotImplementedException();
}
