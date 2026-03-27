// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using KurrentDB.Common.Utils;
using KurrentDB.SchemaRegistry.Infrastructure;
using NJsonSchema;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.BackwardAll;

[Category("JsonSchemaCompatibility")]
public class WhenIncompatible {
	[Test]
	public async Task fails_with_prohibited_changes_across_versions() {
		var v1 = NewJsonSchema()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer)
			.AddRequired("address", JsonObjectType.String)
			.MakeRequired("age");

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			new[] { v1.ToCanonicalJson() },
			SchemaCompatibilityMode.BackwardAll
		);

		result.IsCompatible.ShouldBeFalse();
		result.Errors.Count.ShouldBe(3);
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}
}
