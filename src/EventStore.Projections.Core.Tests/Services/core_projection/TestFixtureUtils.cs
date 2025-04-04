// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public static class TestFixtureUtils {
	public static IEnumerable<ClientMessage.WriteEvents> ToStream(
		this IEnumerable<ClientMessage.WriteEvents> self, string streamId) {
		return self.Where(v => v.EventStreamId == streamId);
	}

	public static List<ClientMessage.WriteEvents> ToStream(
		this List<ClientMessage.WriteEvents> self, string streamId) {
		return self.Where(v => v.EventStreamId == streamId).ToList();
	}

	public static IEnumerable<Event> OfEventType(
		this IEnumerable<ClientMessage.WriteEvents> self, string type) {
		return self.SelectMany(v => v.Events).Where(v => v.EventType == type);
	}

	public static IEnumerable<Event> ExceptOfEventType(
		this IEnumerable<ClientMessage.WriteEvents> self, string type) {
		return self.SelectMany(v => v.Events).Where(v => v.EventType != type);
	}

	public static List<Event> OfEventType(
		this List<ClientMessage.WriteEvents> self, string type) {
		return self.SelectMany(v => v.Events).Where(v => v.EventType == type).ToList();
	}

	public static List<ClientMessage.WriteEvents> WithEventType(
		this List<ClientMessage.WriteEvents> self, string type) {
		return self.Where(v => v.Events.Any(m => m.EventType == type)).ToList();
	}

	public static IEnumerable<T> OfTypes<T, T1, T2>(this IEnumerable<object> source) where T1 : T where T2 : T {
		return source.OfType<T>().Where(v => v is T1 || v is T2);
	}
}
