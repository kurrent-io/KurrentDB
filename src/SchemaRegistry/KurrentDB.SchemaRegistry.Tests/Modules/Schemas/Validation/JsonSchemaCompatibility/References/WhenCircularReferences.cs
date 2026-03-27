// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.References;

[Category("JsonSchemaCompatibility")]
public class WhenCircularReferences {
	[Test]
	public async Task detects_missing_required_field() {
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" },
			                "friend": { "$ref": "#/definitions/person" }
			            }
			        }
			    },
			    "properties": {
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" },
			                "age": { "type": "integer" },
			                "friend": { "$ref": "#/definitions/person" }
			            },
			            "required": ["age"]
			        }
			    },
			    "properties": {
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			""";

		var result = await CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		result.IsCompatible.ShouldBeFalse();
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
		result.Errors.ShouldContain(e => e.PropertyPath == "#/person/age");
	}
}
