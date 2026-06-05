// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Google.Protobuf;

namespace KurrentDB.KontrolPlane.StateMachine;

partial class DatabaseNode : IDatabaseNode {
	EndPoint IDatabaseNode.Address => field ??= Address.ToEndPoint();
}

internal static class DatabaseNodeExtensions {
	public static DatabaseNode? Find(this IReadOnlyList<DatabaseNode> nodes, ByteString address, out int index)
		=> nodes.Find(address.AreEqual, out index);

	private static DatabaseNode? Find(this IReadOnlyList<DatabaseNode> nodes, Predicate<DatabaseNode> predicate, out int index) {
		for (var i = 0; i < nodes.Count; i++) {
			var result = nodes[i];
			if (predicate(result)) {
				index = i;
				return result;
			}
		}

		index = -1;
		return null;
	}

	private static bool AreEqual(this ByteString address, DatabaseNode node) => address.Span.SequenceEqual(node.Address.Span);
}
