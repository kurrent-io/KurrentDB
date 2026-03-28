// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using KurrentDB.Common.Utils;
using KurrentDB.SchemaRegistry.Infrastructure;
using NJsonSchema;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.Full;

[Category("JsonSchemaCompatibility")]
public class WhenCompatible {
	[Test]
	public async Task succeeds_when_adding_optional_field() {
		var v1 = NewJsonSchema()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.AddOptional("email", JsonObjectType.String);

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Full
		);

		result.IsCompatible.ShouldBeTrue();
		result.Errors.ShouldBeEmpty();
	}

	[Test]
	public async Task succeeds_when_deleting_optional_field() {
		var v1 = NewJsonSchema()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.Remove("role");

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Full
		);

		result.IsCompatible.ShouldBeTrue();
		result.Errors.ShouldBeEmpty();
	}
}
