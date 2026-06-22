// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;

namespace KurrentDB.Kontext.Mcp.Inquiry;

public class InquiryManager {
	readonly ConcurrentDictionary<string, Inquiry> _inquiries = new();
	readonly TimeSpan _ttl;

	public InquiryManager(TimeSpan? ttl = null) {
		_ttl = ttl ?? TimeSpan.FromHours(1);
	}

	public Inquiry Create(string workspace) {
		EvictExpired();
		var inquiry = new Inquiry(workspace);
		_inquiries[inquiry.Id] = inquiry;
		return inquiry;
	}

	/// <summary>
	/// Returns the inquiry only if its <see cref="Inquiry.Workspace"/> matches the caller's
	/// current workspace. Returns null on mismatch — same as not-found, so callers learn
	/// nothing about an inquiry owned by another workspace.
	/// </summary>
	public Inquiry? Get(string id, string workspace) {
		if (!_inquiries.TryGetValue(id, out var inquiry))
			return null;

		if (inquiry.Workspace != workspace)
			return null;

		if (IsExpired(inquiry)) {
			_inquiries.TryRemove(id, out _);
			return null;
		}

		inquiry.Touch();
		return inquiry;
	}

	public bool Delete(string id, string workspace) {
		if (!_inquiries.TryGetValue(id, out var inquiry))
			return false;
		if (inquiry.Workspace != workspace)
			return false;
		return _inquiries.TryRemove(id, out _);
	}

	public IReadOnlyCollection<Inquiry> ListAll() {
		EvictExpired();
		return _inquiries.Values.ToList();
	}

	bool IsExpired(Inquiry inquiry) => DateTime.UtcNow - inquiry.LastAccessedAt > _ttl;

	void EvictExpired() {
		foreach (var (id, inquiry) in _inquiries) {
			if (IsExpired(inquiry))
				_inquiries.TryRemove(id, out _);
		}
	}
}