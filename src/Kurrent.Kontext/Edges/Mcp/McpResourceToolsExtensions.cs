// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kurrent.Kontext.Mcp;

/// <summary>
/// Registers every [McpServerTool] method of a tool type with all agent-facing text sourced from
/// <see cref="McpInstructions"/> instead of [Description] attributes: tool descriptions, parameter
/// descriptions (via <see cref="AIJsonSchemaCreateOptions.ParameterDescriptionProvider"/>), and model
/// type/property descriptions (via <see cref="AIJsonSchemaCreateOptions.TransformSchemaNode"/>, which
/// runs for every node in the input and output schema graphs). This helper exists because the SDK's
/// WithTools&lt;T&gt;() cannot carry schema-creation options — they are only reachable through per-tool
/// <see cref="McpServerToolCreateOptions"/>.
/// </summary>
public static class McpResourceToolsExtensions {
	// The SDK defaults with McpJsonContext prepended: model types resolve from source-generated
	// metadata first, MCP protocol types keep resolving from the SDK's own contexts. Lives here
	// rather than on the context class — a static initializer on the partial context can run
	// before the generator's own static fields, observing a half-initialized Default.
	static readonly JsonSerializerOptions ToolSerializerOptions = CreateToolSerializerOptions();

	static JsonSerializerOptions CreateToolSerializerOptions() {
		var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
		options.TypeInfoResolverChain.Insert(0, McpJsonContext.Default);
		// Reject explicit nulls on non-nullable model members with a descriptive JsonException instead
		// of letting them surface as NREs deep in the mappers. Together with the models' settable
		// properties (initializers honored for absent members) this makes the NRT annotations the
		// truth on the wire, so the mapping layer needs no defensive null guards.
		options.RespectNullableAnnotations = true;
		options.MakeReadOnly();
		return options;
	}

	extension(IMcpServerBuilder builder) {
		public IMcpServerBuilder WithToolsFromResources<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TToolType>() where TToolType : class {
			List<McpServerTool> tools = [];

			foreach (var method in typeof(TToolType).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
				if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } attr) continue;

				var name = attr.Name ?? method.Name;

                var options = new McpServerToolCreateOptions {
                    Name                 = name,
                    Description          = McpInstructions.Tool(name),
                    UseStructuredContent = attr.UseStructuredContent,
                    // Source-generated metadata for the model types — required for trimmed/AOT hosts,
                    // where the SDK defaults have no reflection fallback.
                    SerializerOptions    = ToolSerializerOptions,
                    // Unset attribute hints read back as the MCP spec defaults (destructive=true, idempotent=false,
                    // openWorld=true, readOnly=false), so copying them unconditionally emits explicit annotations
                    // with unchanged meaning.
                    ReadOnly            = attr.ReadOnly,
                    Destructive         = attr.Destructive,
                    Idempotent          = attr.Idempotent,
                    OpenWorld           = attr.OpenWorld,
                    SchemaCreateOptions = SchemaOptionsFor(name),
                };

                var tool = McpServerTool.Create(method, CreateTargetFunc(), options);
                
				tools.Add(tool);
			}

			return builder.WithTools(tools);

            Func<RequestContext<CallToolRequestParams>, object> CreateTargetFunc() =>
                request => ActivatorUtilities.GetServiceOrCreateInstance<TToolType>(
                    request.Services ?? throw new InvalidOperationException("Request services are unavailable."));
        }
	}

	static AIJsonSchemaCreateOptions SchemaOptionsFor(string tool) => new() {
		// Model type and property descriptions, injected for every object node in the schema graph.
		// Nodes that already carry a description (e.g. from a leftover attribute) are left untouched.
		TransformSchemaNode = static (ctx, node) => {
			if (node is not JsonObject obj || obj.ContainsKey("description")) return node;

			string? doc;
			if (ctx.PropertyInfo is { } property && ctx.DeclaringType is { } declaring) {
				doc = McpInstructions.Property(declaring, property.Name);

				// Enums only ever appear in schemas as property nodes, so their own type entry (which
				// carries the per-value guidance) would never surface — append it to the property text.
				var underlying = Nullable.GetUnderlyingType(ctx.TypeInfo.Type) ?? ctx.TypeInfo.Type;
				if (underlying.IsEnum && McpInstructions.Type(underlying) is { } enumDoc)
					doc = doc is null ? enumDoc : $"{doc}\n\n{enumDoc}";
			} else {
				doc = McpInstructions.Type(ctx.TypeInfo.Type);
			}

			if (doc is not null) obj["description"] = doc;
			return node;
		},
		ParameterDescriptionProvider = parameter =>
			parameter.Name is null ? null : McpInstructions.Param(tool, parameter.Name),
	};
}
