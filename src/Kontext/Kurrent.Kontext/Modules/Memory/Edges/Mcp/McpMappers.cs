// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Evidence = Kurrent.Kontext.Memory.Mcp.Model.Evidence;
using GitRef = Kurrent.Kontext.Memory.Mcp.Model.GitRef;
using LeanMemory = Kurrent.Kontext.Memory.Mcp.Model.LeanMemory;
using MemoryImportance = Kurrent.Kontext.Memory.Mcp.Model.MemoryImportance;
using MemoryRef = Kurrent.Kontext.Memory.Mcp.Model.MemoryRef;
using MemoryType = Kurrent.Kontext.Memory.Mcp.Model.MemoryType;
using Neighbour = Kurrent.Kontext.Memory.Mcp.Model.Neighbour;
using RetainOutcome = Kurrent.Kontext.Memory.Mcp.Model.RetainOutcome;
using RecalledMemory = Kurrent.Kontext.Memory.Mcp.Model.RecalledMemory;
using RecallResult = Kurrent.Kontext.Memory.Mcp.Model.RecallResult;
using RecollectSort = Kurrent.Kontext.Memory.Mcp.Model.RecollectSort;
using RecordRef = Kurrent.Kontext.Memory.Mcp.Model.RecordRef;
using ReflectResult = Kurrent.Kontext.Memory.Mcp.Model.ReflectResult;
using RetainedMemory = Kurrent.Kontext.Memory.Mcp.Model.RetainedMemory;
using RetainResult = Kurrent.Kontext.Memory.Mcp.Model.RetainResult;
using SortDirection = Kurrent.Kontext.Memory.Mcp.Model.SortDirection;
using StoredMemory = Kurrent.Kontext.Memory.Mcp.Model.StoredMemory;
using Tag = Kurrent.Kontext.Memory.Mcp.Model.Tag;
using TemporalContext = Kurrent.Kontext.Memory.Mcp.Model.TemporalContext;
using WebRef = Kurrent.Kontext.Memory.Mcp.Model.WebRef;

namespace Kurrent.Kontext.Memory.Mcp;

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

	public static RetainOutcome ToModel(Contracts.RetainOutcome v) => (RetainOutcome)(int)v;

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

	public static Contracts.Evidence.Types.MemoryRef ToContract(MemoryRef r) => new() { Id = r.Id };
	public static MemoryRef ToModel(Contracts.Evidence.Types.MemoryRef r) => new() { Id = r.Id };

	public static Contracts.Evidence.Types.RecordRef ToContract(RecordRef r) => new() { Id = r.Id, Position = r.Position };
	public static RecordRef ToModel(Contracts.Evidence.Types.RecordRef r) => new() { Id = r.Id, Position = r.Position };

	public static Contracts.Evidence.Types.GitRef ToContract(GitRef r) => new() {
		Repo      = r.Repo ?? "",
		Commit    = r.Commit,
		Branch    = r.Branch ?? "",
		Path      = r.Path ?? "",
		Symbol    = r.Symbol ?? "",
		LineStart = r.LineStart ?? 0,
		LineEnd   = r.LineEnd ?? 0,
		Excerpt   = r.Excerpt ?? "",
	};

	public static GitRef ToModel(Contracts.Evidence.Types.GitRef r) => new() {
		Repo      = Unset(r.Repo),
		Commit    = r.Commit,
		Branch    = Unset(r.Branch),
		Path      = Unset(r.Path),
		Symbol    = Unset(r.Symbol),
		LineStart = r.LineStart == 0 ? null : r.LineStart,
		LineEnd   = r.LineEnd == 0 ? null : r.LineEnd,
		Excerpt   = Unset(r.Excerpt),
	};

	public static Contracts.Evidence.Types.WebRef ToContract(WebRef r) {
		var proto = new Contracts.Evidence.Types.WebRef { Uri = r.Uri, Title = r.Title ?? "" };

		if (r.RetrievedAt is not null) proto.RetrievedAt = Timestamp.FromDateTimeOffset(r.RetrievedAt.Value);
		proto.Excerpts.AddRange(r.Excerpts);

		return proto;
	}

	public static WebRef ToModel(Contracts.Evidence.Types.WebRef r) => new() {
		Uri         = r.Uri,
		Title       = Unset(r.Title),
		Excerpts    = r.Excerpts.ToList(),
		RetrievedAt = r.RetrievedAt?.ToDateTimeOffset(),
	};

	public static Contracts.Evidence ToContract(Evidence e) => e switch {
		Evidence.ToMemory m => new() { Memory = ToContract(m.Memory) },
		Evidence.ToRecord r => new() { Record = ToContract(r.Record) },
		Evidence.ToGit    g => new() { Git = ToContract(g.Git) },
		Evidence.ToWeb    w => new() { Web = ToContract(w.Web) },
		_ => throw new ArgumentOutOfRangeException(nameof(e)),
	};

	// The contract's `generic` arm has no MCP model (see Model/Evidence.cs), so a memory carrying one
	// is surfaced as its typed neighbours only rather than failing the whole read.
	public static Evidence? ToModel(Contracts.Evidence e) => e.SourceCase switch {
		Contracts.Evidence.SourceOneofCase.Memory => new Evidence.ToMemory { Memory = ToModel(e.Memory) },
		Contracts.Evidence.SourceOneofCase.Record => new Evidence.ToRecord { Record = ToModel(e.Record) },
		Contracts.Evidence.SourceOneofCase.Git    => new Evidence.ToGit { Git = ToModel(e.Git) },
		Contracts.Evidence.SourceOneofCase.Web    => new Evidence.ToWeb { Web = ToModel(e.Web) },
		_ => null,
	};

	public static IReadOnlyList<Evidence> ToModel(IEnumerable<Contracts.Evidence> evidence) =>
		evidence.Select(ToModel).OfType<Evidence>().ToList();

	static string? Unset(string value) => string.IsNullOrEmpty(value) ? null : value;

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

	public static Contracts.Memory ToContract(Model.Memory m) {
		var proto = new Contracts.Memory {
			MemoryType = ToContract(m.Type),
			Content = m.Content,
			Importance = ToContract(m.Importance),
			Reasoning = m.Reasoning,
		};
		if (m.ContentTime is not null) proto.ContentTime = ToContract(m.ContentTime);
		proto.Evidence.AddRange(m.Evidence.Select(ToContract));
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
		Reasoning = m.Reasoning,
		Evidence = ToModel(m.Evidence),
		Tags = m.Tags.Select(ToModel).ToList(),
		ContentTime = ToModel(m.ContentTime),
		Supersedes = m.Supersedes.ToList(),
		RetainedAt = m.RetainedAt?.ToDateTimeOffset() ?? default,
		LastAccessedAt = m.LastAccessedAt?.ToDateTimeOffset(),
		SupersededAt = m.SupersededAt?.ToDateTimeOffset(),
		SupersededBy = string.IsNullOrEmpty(m.SupersededBy) ? null : m.SupersededBy,
	};

	public static LeanMemory ToModel(Contracts.LeanMemory m) => new() {
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
		Outcome = ToModel(m.Outcome),
		MemoryId = m.MemoryId,
		Neighbours = m.Neighbours.Select(ToModel).ToList(),
	};

	public static Neighbour ToModel(Contracts.RetainResponse.Types.Neighbour n) => new() {
		Distance = n.Distance,
		KeywordMatch = n.KeywordMatch,
		Memory = ToModel(n.Memory),
	};

	public static RecallResult ToModel(Contracts.RecallResponse r) => new() {
		QueryId = r.QueryId,
		Memories = r.Memories.Select(ToModel).ToList(),
	};

	public static ReflectResult ToModel(Contracts.ReflectResponse r) => new() {
		QueryId = r.QueryId,
		SynthesizedMemoryIds = r.SynthesizedMemoryIds.ToList(),
		SupersededMemoryIds = r.SupersededMemoryIds.ToList(),
	};

	#endregion
}
