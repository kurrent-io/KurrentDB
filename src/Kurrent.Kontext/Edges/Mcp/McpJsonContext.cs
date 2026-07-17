// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using Kurrent.Kontext.Mcp.Model;
using ModelContextProtocol;

namespace Kurrent.Kontext.Mcp;

/// <summary>
/// Source-generated serialization metadata for the MCP tool model. The registered roots are the tool
/// parameter and return types; the generator walks their property graphs, so the rest of the model
/// (tags, evidence, citations, enums, …) is covered transitively. Generation options mirror
/// <see cref="McpJsonUtilities.DefaultOptions"/> (camelCase, string enums, nulls omitted) so resolving
/// through this context produces the same wire shape as the SDK's reflection fallback — but works
/// trimmed/AOT, where that fallback does not exist.
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	UseStringEnumConverter = true)]
[JsonSerializable(typeof(Memory))]
[JsonSerializable(typeof(IReadOnlyList<Memory>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(RecallOptions))]
[JsonSerializable(typeof(RecollectOptions))]
[JsonSerializable(typeof(ReflectOptions))]
[JsonSerializable(typeof(RetainResult))]
[JsonSerializable(typeof(RetractResult))]
[JsonSerializable(typeof(RecallResult))]
[JsonSerializable(typeof(ReflectResult))]
[JsonSerializable(typeof(StoredMemory))]
[JsonSerializable(typeof(IReadOnlyList<StoredMemory>))]
public sealed partial class McpJsonContext : JsonSerializerContext;
