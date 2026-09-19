// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using System.Text.Json;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.Functions;
using KurrentDB.DuckDB;
using Serilog.Parsing;

namespace KurrentDB.SecondaryIndexing.LogsQuery;

// render_message(@mt, json) -> the rendered CLEF message. A format-specifier token takes its value
// from the CLEF `@r` array (in token order) so numeric/hex/date formats like {n:N0} / {id:B} match
// what the logger wrote; an unformatted token renders from the JSON property. Serilog's parser
// handles tokenization, {{/}} escaping, and alignment.
internal sealed class RenderMessageFunction() : ScalarFunction(FunctionName) {
	public const string FunctionName = "render_message";

	private static readonly MessageTemplateParser Parser = new();

	protected override IReadOnlyList<ParameterDefinition> Parameters => [new(DuckDBType.Varchar), new(DuckDBType.Varchar)];

	protected override LogicalType ReturnType => new(DuckDBType.Varchar);

	protected override void Bind(BindingContext context) {
	}

	protected override void Execute(ExecutionContext context, in DataChunk input, in DataChunk.ColumnBuilder builder) {
		var templates = input[0].BlobRows;
		var payloads = input[1].BlobRows;
		var output = builder;
		var count = (int)input.RowCount;
		for (var row = 0; row < count; row++)
			output.SetValue(row, Render(Utf8(templates[row]), Utf8(payloads[row])).AsSpan());
	}

	private static unsafe string Utf8(DuckDBString value) =>
		value.Length is 0 || value.Data == null
			? string.Empty
			: Encoding.UTF8.GetString(new ReadOnlySpan<byte>((byte*)value.Data, value.Length));

	private static string Render(string? messageTemplate, string? json) {
		if (string.IsNullOrEmpty(messageTemplate))
			return string.Empty;

		try {
			using var document = json is null ? null : JsonDocument.Parse(json);
			var root = document?.RootElement;
			var renderings = Renderings(root);

			var output = new StringBuilder(messageTemplate.Length + 16);
			var renderingIndex = 0;
			foreach (var token in Parser.Parse(messageTemplate).Tokens) {
				switch (token) {
					case TextToken text:
						output.Append(text.Text);
						break;
					case PropertyToken { Format: not null } formatted:
						if (renderings is { } r && renderingIndex < r.GetArrayLength() && r[renderingIndex].ValueKind is JsonValueKind.String)
							output.Append(r[renderingIndex].GetString());
						else
							AppendProperty(output, root, formatted);
						renderingIndex++;
						break;
					case PropertyToken property:
						AppendProperty(output, root, property);
						break;
				}
			}

			return output.ToString();
		} catch (Exception) {
			// Never fail the whole query over one odd line: degrade to the raw template.
			return messageTemplate;
		}
	}

	private static JsonElement? Renderings(JsonElement? root) =>
		root is { ValueKind: JsonValueKind.Object } obj
		&& obj.TryGetProperty("@r", out var r)
		&& r.ValueKind is JsonValueKind.Array
			? r
			: null;

	private static void AppendProperty(StringBuilder output, JsonElement? root, PropertyToken token) {
		string? value = null;
		if (root is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(token.PropertyName, out var v))
			value = v.ValueKind is JsonValueKind.String ? v.GetString() : v.GetRawText();

		if (value is null) {
			output.Append(token.ToString()); // unresolved property -> the raw {token}
			return;
		}

		if (token.Alignment is { } alignment && alignment.Width > value.Length)
			value = alignment.Direction is AlignmentDirection.Left
				? value.PadRight(alignment.Width)
				: value.PadLeft(alignment.Width);

		output.Append(value);
	}
}

// Registered once (not per query): the scalar function goes in the DuckDB instance catalog (shared
// across pooled connections) and registration isn't idempotent.
internal sealed class RenderMessageSetup : DuckDBOneTimeSetup {
	protected override void ExecuteCore(DuckDBAdvancedConnection connection) =>
		new RenderMessageFunction().Register(connection);
}
