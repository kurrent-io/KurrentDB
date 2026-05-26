// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;

namespace KurrentDB.Kontext.Mcp.Inquiry;

public class Inquiry {
	readonly ConcurrentDictionary<long, EventResult> _events = new();
	readonly ConcurrentDictionary<long, byte> _forgotten = new();

	public string Id { get; } = Guid.NewGuid().ToString("N");
	public string Workspace { get; }
	public DateTime CreatedAt { get; } = DateTime.UtcNow;
	public DateTime LastAccessedAt { get; private set; } = DateTime.UtcNow;

	public Inquiry(string workspace) {
		if (string.IsNullOrEmpty(workspace))
			throw new ArgumentException("Inquiry workspace must be a non-empty identifier.", nameof(workspace));
		Workspace = workspace;
	}

	/// <summary>Working set: all events minus forgotten.</summary>
	public ICollection<EventResult> Events => _events.Values;

	public int WorkingSetSize => _events.Count;

	public void Add(EventResult evt) => _events.TryAdd(evt.Id, evt);

	public bool Contains(EventResult evt) => _events.ContainsKey(evt.Id);

	public bool IsForgotten(EventResult evt) => _forgotten.ContainsKey(evt.Id);

	public bool TryForget(long eventId) {
		var removed = _events.TryRemove(eventId, out _);
		_forgotten.TryAdd(eventId, 0);
		return removed;
	}

	public void Touch() => LastAccessedAt = DateTime.UtcNow;
}