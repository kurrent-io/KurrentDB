// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using Google.Protobuf.Collections;
using Contracts = KurrentDB.Protocol.Registry.V2;

namespace KurrentDB.SchemaRegistry.Data;

internal static class SchemaQueriesMapping {
	internal static MapField<string, string?> MapToTags(string source) =>
		source == "{}" ? new() : new() { JsonSerializer.Deserialize<Dictionary<string, string?>>(source) };

	public static Contracts.CheckSchemaCompatibilityResponse MapToSchemaCompatibilityResult(Kurrent.Surge.Schema.Validation.SchemaCompatibilityResult result, string schemaVersionId) {
		if (result.Errors.Any()) {
			return new() { Failure = new() { Errors = { result.Errors.Select(MapToSchemaValidationError) } } };
		}

		return new() { Success = new() { SchemaVersionId = schemaVersionId } };

		static Contracts.SchemaCompatibilityError MapToSchemaValidationError(Kurrent.Surge.Schema.Validation.SchemaCompatibilityError value) =>
			new() {
				Kind = (Contracts.SchemaCompatibilityErrorKind)value.Kind,
				Details = value.Details,
				PropertyPath = value.PropertyPath,
				OriginalType = value.OriginalType.ToString(),
				NewType = value.NewType.ToString()
			};
	}
}
