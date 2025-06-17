// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MemberCanBeMadeStatic.Global

using KurrentDB.Protocol.Registry.V2;
using static KurrentDB.SchemaRegistry.Data.SchemaQueriesMapping;
using SchemaCompatibilityError = Kurrent.Surge.Schema.Validation.SchemaCompatibilityError;
using SchemaCompatibilityErrorKind = Kurrent.Surge.Schema.Validation.SchemaCompatibilityErrorKind;
using SchemaCompatibilityResult = Kurrent.Surge.Schema.Validation.SchemaCompatibilityResult;

namespace Kurrent.Surge.Core.Tests.Schema.Validation;

public class SchemaCompatibilityValidationTests {
	[Test]
	public void MapToSchemaCompatibilityResult_preserves_error_kinds() {
		// Arrange
		var schemaVersionId = Guid.NewGuid().ToString();

		List<SchemaCompatibilityError> errors = [
			new() { Kind = SchemaCompatibilityErrorKind.MissingRequiredProperty },
			new() { Kind = SchemaCompatibilityErrorKind.NewRequiredProperty },
			new() { Kind = SchemaCompatibilityErrorKind.ArrayTypeIncompatibility },
			new() { Kind = SchemaCompatibilityErrorKind.IncompatibleTypeChange },
			new() { Kind = SchemaCompatibilityErrorKind.RemovedProperty },
			new() { Kind = SchemaCompatibilityErrorKind.OptionalToRequired }
		];

		var expectedKinds = new[] {
			KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind.MissingRequiredProperty,
			KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind.NewRequiredProperty,
			KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind.ArrayTypeIncompatibility,
			KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind.IncompatibleTypeChange,
			KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind.RemovedProperty,
			KurrentDB.Protocol.Registry.V2.SchemaCompatibilityErrorKind.OptionalToRequired
		};

		// Act
		var result = MapToSchemaCompatibilityResult(SchemaCompatibilityResult.Incompatible(errors), schemaVersionId);

		// Assert
		result.Failure.Errors
			.Select(e => e.Kind)
			.Should()
			.ContainInOrder(expectedKinds);
	}
}
