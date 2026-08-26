// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;
using Kurrent.Kontext.Memory.Mcp.Model;
using ModelContextProtocol;
using MemoryModel = Kurrent.Kontext.Memory.Mcp.Model.Memory;
using RecordsQueryResult = Kurrent.Kontext.Records.Mcp.Model.QueryResult;
using RecordsSearchOptions = Kurrent.Kontext.Records.Mcp.Model.SearchOptions;
using RecordsSearchResult = Kurrent.Kontext.Records.Mcp.Model.SearchResult;

namespace Kurrent.Kontext.Mcp;

/// <summary>
/// Source-generated serialization metadata for the MCP tool model. The registered roots are the tool
/// parameter and return types of EVERY tool type the server exposes; the generator walks their property
/// graphs, so the rest of the model (tags, evidence, citations, records, enums, …) is covered
/// transitively. Generation options mirror
/// <see cref="McpJsonUtilities.DefaultOptions"/> (camelCase, string enums, nulls omitted) so resolving
/// through this context produces the same wire shape as the SDK's reflection fallback — but works
/// trimmed/AOT, where that fallback does not exist.
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	UseStringEnumConverter = true)]
[JsonSerializable(typeof(MemoryModel))]
[JsonSerializable(typeof(IReadOnlyList<MemoryModel>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(RecallOptions))]
[JsonSerializable(typeof(RecollectOptions))]
[JsonSerializable(typeof(RetainResult))]
[JsonSerializable(typeof(RecallResult))]
[JsonSerializable(typeof(ReinforceResult))]
[JsonSerializable(typeof(StoredMemory))]
[JsonSerializable(typeof(IReadOnlyList<StoredMemory>))]
[JsonSerializable(typeof(RecordsSearchOptions))]
[JsonSerializable(typeof(RecordsSearchResult))]
[JsonSerializable(typeof(RecordsQueryResult))]
public sealed partial class McpJsonContext : JsonSerializerContext;
