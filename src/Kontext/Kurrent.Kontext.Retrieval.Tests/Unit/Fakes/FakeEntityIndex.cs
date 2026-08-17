// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Retrieval.Tests.Fakes;

/// <summary>
/// An in-memory entities read model: names map to entities, entities hold a mention counter, notes
/// join pairs, and mentions file memories. Reads answer only what was ASKED for — the note and
/// mention walks filter to the requested ids, exactly as SQL would, so a stage that takes a hop it
/// was not given can be caught.
/// </summary>
sealed class FakeEntityIndex : IEntityIndex {
	readonly Dictionary<string, List<EntityMatch>> _named    = new(StringComparer.Ordinal);
	readonly List<EntityNote>                      _notes    = [];
	readonly List<EntityMention>                   _mentions = [];

	public int MatchCalls   { get; private set; }
	public int NoteCalls    { get; private set; }
	public int MentionCalls { get; private set; }

	/// <summary>Files an entity under the surface a question has to contain for it to be recognized.</summary>
	public FakeEntityIndex Named(string surface, string entityId, long mentionCount = 1) {
		if (!_named.TryGetValue(surface, out var matches))
			_named[surface] = matches = [];

		matches.Add(new(entityId, mentionCount));

		return this;
	}

	public FakeEntityIndex Note(string source, string target, double confidence) {
		_notes.Add(new(source, target, confidence));
		return this;
	}

	public FakeEntityIndex Mentions(string entityId, params string[] memoryIds) {
		_mentions.AddRange(memoryIds.Select(memoryId => new EntityMention(entityId, memoryId)));
		return this;
	}

	public ValueTask<IReadOnlyList<EntityMatch>> MatchAsync(string question, CancellationToken ct = default) {
		MatchCalls++;

		IReadOnlyList<EntityMatch> matches = [
			.. _named
				.Where(named => question.Contains(named.Key, StringComparison.OrdinalIgnoreCase))
				.SelectMany(named => named.Value),
		];

		return new(matches);
	}

	public ValueTask<IReadOnlyList<EntityNote>> ListNotesAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default) {
		NoteCalls++;

		IReadOnlyList<EntityNote> notes = [
			.. _notes.Where(note => entityIds.Contains(note.SourceEntityId) || entityIds.Contains(note.TargetEntityId)),
		];

		return new(notes);
	}

	public ValueTask<IReadOnlyList<EntityMention>> ListMentionsAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default) {
		MentionCalls++;

		IReadOnlyList<EntityMention> mentions = [.. _mentions.Where(mention => entityIds.Contains(mention.EntityId))];

		return new(mentions);
	}
}
