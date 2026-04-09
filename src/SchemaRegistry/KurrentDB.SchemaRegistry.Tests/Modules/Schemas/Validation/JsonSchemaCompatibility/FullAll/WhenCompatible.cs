// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using KurrentDB.Common.Utils;
using KurrentDB.SchemaRegistry.Infrastructure;
using NJsonSchema;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.FullAll;

[Category("JsonSchemaCompatibility")]
public class WhenCompatible {
	[Test]
	public async Task succeeds_with_allowed_changes_across_multiple_versions() {
		var v1 = NewJsonSchema()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.Remove("age");

		var v3 = v2
			.AddOptional("email", JsonObjectType.String);

		var result = await CheckCompatibility(
			v3.ToCanonicalJson(),
			new[] { v1.ToCanonicalJson(), v2.ToCanonicalJson() },
			SchemaCompatibilityMode.FullAll
		);

		result.IsCompatible.ShouldBeTrue();
		result.Errors.ShouldBeEmpty();
	}
}
