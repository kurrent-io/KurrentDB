// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Contracts = Kurrent.Kontext.Contracts;
using Core = Kurrent.Kontext.Core;

namespace KurrentDB.Kontext.Reloaded.Edges.Grpc;

/// <summary>
/// Maps between the generated v3 gRPC contract messages (<c>Kurrent.Kontext.Contracts</c>) and the
/// transport-agnostic domain (<c>Kurrent.Kontext.Core</c>). All protobuf coupling lives here so the core and the
/// service stay wire-free. Both namespaces declare colliding type names (<c>Memory</c>, <c>Tag</c>, …), so they
/// are aliased throughout.
/// </summary>
static class MemoryMappers {
	#region ->> Enums <<-

	// Domain and contract enums are defined with identical numeric values (both mirror the proto), so a cast
	// bridges them even where the C# names differ — the proto keeps its SENTIMENT_/URGENCY_ prefix, the domain
	// does not. If the two ever diverge in value this is where it breaks, so keep the value lists in lock-step.
	public static Core.MemoryType ToDomain(Contracts.MemoryType v) => (Core.MemoryType)(int)v;
	public static Contracts.MemoryType ToContract(Core.MemoryType v) => (Contracts.MemoryType)(int)v;

	public static Core.MemoryImportance ToDomain(Contracts.MemoryImportance v) => (Core.MemoryImportance)(int)v;
	public static Contracts.MemoryImportance ToContract(Core.MemoryImportance v) => (Contracts.MemoryImportance)(int)v;

	public static Core.MemorySentiment ToDomain(Contracts.MemorySentiment v) => (Core.MemorySentiment)(int)v;
	public static Contracts.MemorySentiment ToContract(Core.MemorySentiment v) => (Contracts.MemorySentiment)(int)v;

	public static Core.MemoryUrgency ToDomain(Contracts.MemoryUrgency v) => (Core.MemoryUrgency)(int)v;
	public static Contracts.MemoryUrgency ToContract(Core.MemoryUrgency v) => (Contracts.MemoryUrgency)(int)v;

	public static Core.RecollectSort ToDomain(Contracts.RecollectSort v) => (Core.RecollectSort)(int)v;
	public static Core.SortDirection ToDomain(Contracts.SortDirection v) => (Core.SortDirection)(int)v;

	#endregion

	#region ->> Values (tag, provenance, temporal) <<-

	public static Core.Tag ToDomain(Contracts.Tag t) => Core.Tag.Create(t.Value, t.Scope);
	public static Contracts.Tag ToContract(Core.Tag t) => new() { Value = t.Value, Scope = t.Scope };

	public static Core.MemoryRef ToDomain(Contracts.Evidence.Types.MemoryRef r) => new(Core.MemoryId.Parse(r.Id), r.Position);
	public static Contracts.Evidence.Types.MemoryRef ToContract(Core.MemoryRef r) => new() { Id = r.Id.ToString(), Position = r.Position };

	public static Core.RecordRef ToDomain(Contracts.Evidence.Types.RecordRef r) => new(r.Id, r.Position);
	public static Contracts.Evidence.Types.RecordRef ToContract(Core.RecordRef r) => new() { Id = r.Id, Position = r.Position };

	public static Core.Citation ToDomain(Contracts.Evidence.Types.Citation c) => c.CitedCase switch {
		Contracts.Evidence.Types.Citation.CitedOneofCase.Memory => new Core.Citation.ToMemory(ToDomain(c.Memory)),
		Contracts.Evidence.Types.Citation.CitedOneofCase.Record => new Core.Citation.ToRecord(ToDomain(c.Record)),
		_ => throw new ArgumentException("Citation has no cited source set."),
	};

	public static Contracts.Evidence.Types.Citation ToContract(Core.Citation c) => c switch {
		Core.Citation.ToMemory m => new() { Memory = ToContract(m.Memory) },
		Core.Citation.ToRecord r => new() { Record = ToContract(r.Record) },
		_ => throw new ArgumentOutOfRangeException(nameof(c)),
	};

	public static Core.Evidence ToDomain(Contracts.Evidence? e) =>
		e is null ? Core.Evidence.None : new Core.Evidence {
			Reasoning = e.Reasoning,
			Citations = e.Citations.Select(ToDomain).ToList(),
		};

	// Empty evidence maps to an unset proto field, so a raw observation carries no evidence message on the wire.
	public static Contracts.Evidence? ToContract(Core.Evidence e) =>
		e.Reasoning.Length == 0 && e.Citations.Count == 0
			? null
			: new Contracts.Evidence {
				Reasoning = e.Reasoning,
				Citations = { e.Citations.Select(ToContract) },
			};

	public static Core.TemporalContext? ToDomain(Contracts.TemporalContext? t) =>
		t is null ? null : new Core.TemporalContext(t.PerceivedStart?.ToDateTimeOffset() ?? default, t.PerceivedEnd?.ToDateTimeOffset());

	public static Contracts.TemporalContext ToContract(Core.TemporalContext t) {
		var proto = new Contracts.TemporalContext { PerceivedStart = ToTimestamp(t.From) };
		if (t.To is not null) proto.PerceivedEnd = ToTimestamp(t.To.Value);
		return proto;
	}

	static Timestamp ToTimestamp(DateTimeOffset value) => Timestamp.FromDateTimeOffset(value);

	#endregion

	#region ->> Memory (command in) <<-

	public static Core.Memory ToDomain(Contracts.Memory m) => new() {
		Id = string.IsNullOrEmpty(m.MemoryId) ? null : Core.MemoryId.Parse(m.MemoryId),
		Type = ToDomain(m.MemoryType),
		Content = m.Content,
		Importance = ToDomain(m.Importance),
		Evidence = ToDomain(m.Evidence),
		Tags = m.Tags.Select(ToDomain).ToList(),
		Sentiment = ToDomain(m.Sentiment),
		Urgency = ToDomain(m.Urgency),
		Validity = ToDomain(m.Validity),
		Supersedes = m.Supersedes.Select(Core.MemoryId.Parse).ToList(),
	};

	#endregion

	#region ->> Read models (out) <<-

	public static Contracts.StoredMemory ToContract(Core.StoredMemory m) {
		var proto = new Contracts.StoredMemory {
			MemoryId = m.MemoryId.ToString(),
			MemoryType = ToContract(m.MemoryType),
			Content = m.Content,
			Importance = ToContract(m.Importance),
			Sentiment = ToContract(m.Sentiment),
			Urgency = ToContract(m.Urgency),
			RetainedAt = ToTimestamp(m.RetainedAt),
			Tags = { m.Tags.Select(ToContract) },
			Supersedes = { m.Supersedes.Select(id => id.ToString()) },
		};
		
		if (ToContract(m.Evidence) is { } evidence) proto.Evidence = evidence;
		if (m.Validity is not null) proto.Validity = ToContract(m.Validity);
		if (m.LastAccessedAt is not null) proto.LastAccessedAt = ToTimestamp(m.LastAccessedAt.Value);
		if (m.RetractedAt is not null) proto.RetractedAt = ToTimestamp(m.RetractedAt.Value);
		if (m.SupersededAt is not null) proto.SupersededAt = ToTimestamp(m.SupersededAt.Value);
		if (m.SupersededBy is not null) proto.SupersededBy = m.SupersededBy.Value.ToString();
		return proto;
	}

	public static Contracts.RecallResponse.Types.RecalledMemory.Types.LeanMemory ToContract(Core.LeanMemory m) => new() {
		MemoryId = m.MemoryId.ToString(),
		MemoryType = ToContract(m.MemoryType),
		Content = m.Content,
		Importance = ToContract(m.Importance),
		RetainedAt = ToTimestamp(m.RetainedAt),
		Tags = { m.Tags.Select(ToContract) },
	};

	public static Contracts.RecallResponse.Types.RecalledMemory ToContract(Core.RecalledMemory hit) => hit switch {
		Core.RecalledMemory.Lean l => new() { Score = l.Score, Lean = ToContract(l.Memory) },
		Core.RecalledMemory.Full f => new() { Score = f.Score, Full = ToContract(f.Memory) },
		_ => throw new ArgumentOutOfRangeException(nameof(hit)),
	};

	#endregion

	#region ->> Results (out) <<-

	public static Contracts.RetainResponse ToContract(Core.RetainResult r) => new() {
		Results = { r.Results.Select(ToContract) },
	};

	public static Contracts.RetainResponse.Types.RetainResult ToContract(Core.RetainedMemory m) => new() {
		MemoryId = m.MemoryId.ToString(),
		Related = { m.Related.Select(ToContract) },
	};

	public static Contracts.RetainResponse.Types.RelatedMemory ToContract(Core.RelatedMemory r) => new() {
		MemoryId = r.MemoryId.ToString(),
		Similarity = r.Similarity,
	};

	public static Contracts.RetractResponse ToContract(Core.RetractResult r) => new() {
		RetractedMemoryIds = { r.RetractedMemoryIds.Select(id => id.ToString()) },
	};

	public static Contracts.RecallResponse ToContract(Core.RecallResult r) => new() {
		QueryId = r.QueryId.ToString(),
		Memories = { r.Memories.Select(ToContract) },
	};

	public static Contracts.ReflectResponse ToContract(Core.ReflectResult r) => new() {
		QueryId = r.QueryId.ToString(),
		SynthesizedMemoryIds = { r.SynthesizedMemoryIds.Select(id => id.ToString()) },
		SupersededMemoryIds = { r.SupersededMemoryIds.Select(id => id.ToString()) },
		RetractedMemoryIds = { r.RetractedMemoryIds.Select(id => id.ToString()) },
	};

	#endregion
}
