// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Citation = Kurrent.Kontext.Mcp.Model.Citation;
using Evidence = Kurrent.Kontext.Mcp.Model.Evidence;
using LeanMemory = Kurrent.Kontext.Mcp.Model.LeanMemory;
using Memory = Kurrent.Kontext.Mcp.Model.Memory;
using MemoryImportance = Kurrent.Kontext.Mcp.Model.MemoryImportance;
using MemoryRef = Kurrent.Kontext.Mcp.Model.MemoryRef;
using MemorySentiment = Kurrent.Kontext.Mcp.Model.MemorySentiment;
using MemoryType = Kurrent.Kontext.Mcp.Model.MemoryType;
using MemoryUrgency = Kurrent.Kontext.Mcp.Model.MemoryUrgency;
using RecalledMemory = Kurrent.Kontext.Mcp.Model.RecalledMemory;
using RecallResult = Kurrent.Kontext.Mcp.Model.RecallResult;
using RecollectSort = Kurrent.Kontext.Mcp.Model.RecollectSort;
using RecordRef = Kurrent.Kontext.Mcp.Model.RecordRef;
using ReflectResult = Kurrent.Kontext.Mcp.Model.ReflectResult;
using RelatedMemory = Kurrent.Kontext.Mcp.Model.RelatedMemory;
using RetainedMemory = Kurrent.Kontext.Mcp.Model.RetainedMemory;
using RetainResult = Kurrent.Kontext.Mcp.Model.RetainResult;
using RetractResult = Kurrent.Kontext.Mcp.Model.RetractResult;
using SortDirection = Kurrent.Kontext.Mcp.Model.SortDirection;
using StoredMemory = Kurrent.Kontext.Mcp.Model.StoredMemory;
using Tag = Kurrent.Kontext.Mcp.Model.Tag;
using TemporalContext = Kurrent.Kontext.Mcp.Model.TemporalContext;

namespace Kurrent.Kontext.Mcp;

/// <summary>
/// Maps between the MCP edge's HTTP-friendly model (<c>Edges.Mcp.Model</c>, ids as plain strings) and the
/// gRPC canonical contract messages (<c>Kurrent.Kontext.Contracts</c>). The canonical model is the core's
/// language now, so this is the only mapping layer left — it converts the MCP tool shapes into contract
/// requests on the way in and folds contract responses back into the MCP model on the way out. Both sides
/// declare colliding type names (<c>Memory</c>, <c>Tag</c>, …), so they are aliased throughout.
/// </summary>
static class McpMappers {
	#region ->> Enums (cast — both sides mirror the proto's numeric values) <<-

	public static Contracts.MemoryType ToContract(MemoryType v) => (Contracts.MemoryType)(int)v;
	public static MemoryType ToModel(Contracts.MemoryType v) => (MemoryType)(int)v;

	public static Contracts.MemoryImportance ToContract(MemoryImportance v) => (Contracts.MemoryImportance)(int)v;
	public static MemoryImportance ToModel(Contracts.MemoryImportance v) => (MemoryImportance)(int)v;

	public static Contracts.MemorySentiment ToContract(MemorySentiment v) => (Contracts.MemorySentiment)(int)v;
	public static MemorySentiment ToModel(Contracts.MemorySentiment v) => (MemorySentiment)(int)v;

	public static Contracts.MemoryUrgency ToContract(MemoryUrgency v) => (Contracts.MemoryUrgency)(int)v;
	public static MemoryUrgency ToModel(Contracts.MemoryUrgency v) => (MemoryUrgency)(int)v;

	public static Contracts.RecollectSort ToContract(RecollectSort v) => (Contracts.RecollectSort)(int)v;
	public static Contracts.SortDirection ToContract(SortDirection v) => (Contracts.SortDirection)(int)v;

	#endregion

	#region ->> Values (tag, evidence, temporal) <<-

	// Input shapes arrive as agent JSON, but the model's NRT annotations are enforced on the wire:
	// the tool serializer options set RespectNullableAnnotations (explicit null on a non-nullable
	// member is rejected as a protocol error) and the models use settable properties, whose
	// initializers the source generator honors for absent members. Non-nullable members are therefore
	// trustworthy here; nullable ones (ids, query ids) mean "unset" and map to proto empty strings.
	public static Contracts.Tag ToContract(Tag t) => new() { Value = t.Value, Scope = t.Scope };
	public static Tag ToModel(Contracts.Tag t) => new() { Value = t.Value, Scope = t.Scope };

	public static Contracts.Evidence.Types.MemoryRef ToContract(MemoryRef r) => new() { Id = r.Id, Position = r.Position ?? -1 };
	public static MemoryRef ToModel(Contracts.Evidence.Types.MemoryRef r) => new() { Id = r.Id, Position = r.Position };

	public static Contracts.Evidence.Types.RecordRef ToContract(RecordRef r) => new() { Id = r.Id, Position = r.Position };
	public static RecordRef ToModel(Contracts.Evidence.Types.RecordRef r) => new() { Id = r.Id, Position = r.Position };

	public static Contracts.Evidence.Types.Citation ToContract(Citation c) => c switch {
		Citation.ToMemory m => new() { Memory = ToContract(m.Memory) },
		Citation.ToRecord r => new() { Record = ToContract(r.Record) },
		_ => throw new ArgumentOutOfRangeException(nameof(c)),
	};

	public static Citation ToModel(Contracts.Evidence.Types.Citation c) => c.CitedCase switch {
		Contracts.Evidence.Types.Citation.CitedOneofCase.Memory => new Citation.ToMemory { Memory = ToModel(c.Memory) },
		Contracts.Evidence.Types.Citation.CitedOneofCase.Record => new Citation.ToRecord { Record = ToModel(c.Record) },
		_ => throw new ArgumentException("Citation has no cited source set."),
	};

	// Null or empty evidence maps to an unset proto field, so a raw observation carries no evidence message on the wire.
	public static Contracts.Evidence? ToContract(Evidence? e) =>
		e is null || (e.Reasoning.Length == 0 && e.Citations.Count == 0)
			? null
			: new Contracts.Evidence {
				Reasoning = e.Reasoning,
				Citations = { e.Citations.Select(ToContract) },
			};

	public static Evidence? ToModel(Contracts.Evidence? e) =>
		e is null ? null : new Evidence {
			Reasoning = e.Reasoning,
			Citations = e.Citations.Select(ToModel).ToList(),
		};

	public static Contracts.TemporalContext ToContract(TemporalContext t) {
		var proto = new Contracts.TemporalContext { PerceivedStart = Timestamp.FromDateTimeOffset(t.From) };
		if (t.To is not null) proto.PerceivedEnd = Timestamp.FromDateTimeOffset(t.To.Value);
		return proto;
	}

	public static TemporalContext? ToModel(Contracts.TemporalContext? t) =>
		t is null ? null : new TemporalContext {
			From = t.PerceivedStart?.ToDateTimeOffset() ?? default,
			To = t.PerceivedEnd?.ToDateTimeOffset(),
		};

	#endregion

	#region ->> Memory (command in) <<-

	public static Contracts.Memory ToContract(Memory m) {
		var proto = new Contracts.Memory {
			// Empty id ⇒ server assigns; the MCP model carries a nullable string for the same "unset" intent.
			MemoryId = m.Id ?? "",
			MemoryType = ToContract(m.Type),
			Content = m.Content,
			Importance = ToContract(m.Importance),
			Sentiment = ToContract(m.Sentiment),
			Urgency = ToContract(m.Urgency),
		};
		if (ToContract(m.Evidence) is { } evidence) proto.Evidence = evidence;
		if (m.Validity is not null) proto.Validity = ToContract(m.Validity);
		proto.Tags.AddRange(m.Tags.Select(ToContract));
		proto.Supersedes.AddRange(m.Supersedes);
		return proto;
	}

	#endregion

	#region ->> Read models (out) <<-

	public static StoredMemory ToModel(Contracts.StoredMemory m) => new() {
		MemoryId = m.MemoryId,
		MemoryType = ToModel(m.MemoryType),
		Content = m.Content,
		Importance = ToModel(m.Importance),
		Evidence = ToModel(m.Evidence),
		Tags = m.Tags.Select(ToModel).ToList(),
		Sentiment = ToModel(m.Sentiment),
		Urgency = ToModel(m.Urgency),
		Validity = ToModel(m.Validity),
		Supersedes = m.Supersedes.ToList(),
		RetainedAt = m.RetainedAt?.ToDateTimeOffset() ?? default,
		LastAccessedAt = m.LastAccessedAt?.ToDateTimeOffset(),
		RetractedAt = m.RetractedAt?.ToDateTimeOffset(),
		SupersededAt = m.SupersededAt?.ToDateTimeOffset(),
		SupersededBy = string.IsNullOrEmpty(m.SupersededBy) ? null : m.SupersededBy,
	};

	public static LeanMemory ToModel(Contracts.RecallResponse.Types.RecalledMemory.Types.LeanMemory m) => new() {
		MemoryId = m.MemoryId,
		MemoryType = ToModel(m.MemoryType),
		Content = m.Content,
		Tags = m.Tags.Select(ToModel).ToList(),
		Importance = ToModel(m.Importance),
		RetainedAt = m.RetainedAt?.ToDateTimeOffset() ?? default,
	};

	public static RecalledMemory ToModel(Contracts.RecallResponse.Types.RecalledMemory hit) => hit.BodyCase switch {
		Contracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Lean => new RecalledMemory.Lean { Score = hit.Score, Memory = ToModel(hit.Lean) },
		Contracts.RecallResponse.Types.RecalledMemory.BodyOneofCase.Full => new RecalledMemory.Full { Score = hit.Score, Memory = ToModel(hit.Full) },
		_ => throw new ArgumentException("RecalledMemory has no body set."),
	};

	#endregion

	#region ->> Results (out) <<-

	public static RetainResult ToModel(Contracts.RetainResponse r) => new() {
		Results = r.Results.Select(ToModel).ToList(),
	};

	public static RetainedMemory ToModel(Contracts.RetainResponse.Types.RetainResult m) => new() {
		MemoryId = m.MemoryId,
		Related = m.Related.Select(ToModel).ToList(),
	};

	public static RelatedMemory ToModel(Contracts.RetainResponse.Types.RelatedMemory r) => new() {
		MemoryId = r.MemoryId,
		Similarity = r.Similarity,
	};

	public static RetractResult ToModel(Contracts.RetractResponse r) => new() {
		RetractedMemoryIds = r.RetractedMemoryIds.ToList(),
	};

	public static RecallResult ToModel(Contracts.RecallResponse r) => new() {
		QueryId = r.QueryId,
		Memories = r.Memories.Select(ToModel).ToList(),
	};

	public static ReflectResult ToModel(Contracts.ReflectResponse r) => new() {
		QueryId = r.QueryId,
		SynthesizedMemoryIds = r.SynthesizedMemoryIds.ToList(),
		SupersededMemoryIds = r.SupersededMemoryIds.ToList(),
		RetractedMemoryIds = r.RetractedMemoryIds.ToList(),
	};

	#endregion
}
