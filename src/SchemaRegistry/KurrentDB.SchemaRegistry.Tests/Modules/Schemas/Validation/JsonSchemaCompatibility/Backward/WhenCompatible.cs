// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using KurrentDB.Common.Utils;
using KurrentDB.SchemaRegistry.Infrastructure;
using NJsonSchema;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.Backward;

[Category("JsonSchemaCompatibility")]
public class WhenCompatible {
	[Test]
	public async Task succeeds_when_adding_optional_field() {
		var v1 = NewJsonSchema();

		var v2 = v1
			.AddOptional("email", JsonObjectType.String);

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeTrue();
	}

	[Test]
	public async Task succeeds_when_making_required_field_optional() {
		var v1 = NewJsonSchema()
			.AddOptional("email", JsonObjectType.String)
			.AddRequired("age", JsonObjectType.Integer);

		var v2 = v1
			.MakeOptional("age");

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeTrue();
	}

	[Test]
	public async Task succeeds_when_widening_union_field() {
		var v1 = NewJsonSchema()
			.AddRequired("gender", JsonObjectType.Integer);

		var v2 = v1
			.WidenType("gender", JsonObjectType.Integer);

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeTrue();
	}

	[Test]
	public async Task succeeds_when_deleting_optional_field() {
		var v1 = NewJsonSchema()
			.AddOptional("email", JsonObjectType.String);

		var v2 = v1
			.Remove("email");

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeTrue();
	}

	[Test]
	public async Task succeeds_when_deleting_field_with_default_value() {
		var v1 = NewJsonSchema()
			.AddOptional("role", JsonObjectType.String, "admin");

		var v2 = v1
			.Remove("role");

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeTrue();
	}
}
