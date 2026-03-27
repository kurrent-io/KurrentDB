// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using KurrentDB.Common.Utils;
using KurrentDB.SchemaRegistry.Infrastructure;
using NJsonSchema;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.Backward;

[Category("JsonSchemaCompatibility")]
public class WhenIncompatible {
	[Test]
	public async Task fails_when_deleting_required_field() {
		var v1 = NewJsonSchema()
			.AddRequired("role", JsonObjectType.String);

		var v2 = v1
			.Remove("role");

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeFalse();
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
	}

	[Test]
	public async Task fails_when_making_optional_field_required() {
		var v1 = NewJsonSchema()
			.AddOptional("role", JsonObjectType.String);

		var v2 = v1
			.MakeRequired("role");

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeFalse();
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	[Test]
	public async Task fails_when_changing_field_type() {
		var v1 = NewJsonSchema()
			.AddOptional("role", JsonObjectType.String);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer);

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeFalse();
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}

	[Test]
	public async Task fails_when_adding_required_field() {
		var v1 = NewJsonSchema();

		var v2 = v1
			.AddRequired("role", JsonObjectType.Integer);

		var result = await CheckCompatibility(
			v2.ToCanonicalJson(),
			v1.ToCanonicalJson(),
			SchemaCompatibilityMode.Backward
		);

		result.IsCompatible.ShouldBeFalse();
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}
}
