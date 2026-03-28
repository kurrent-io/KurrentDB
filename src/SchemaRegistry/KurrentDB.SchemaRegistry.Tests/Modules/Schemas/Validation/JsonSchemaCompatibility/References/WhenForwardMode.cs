// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.References;

[Category("JsonSchemaCompatibility")]
public class WhenForwardMode {
	[Test]
	public async Task compatible_when_removing_optional_field() {
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" }
			            }
			        }
			    },
			    "properties": {
			        "field1": { "type": "string" },
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
			                "age": { "type": "integer" }
			            }
			        }
			    },
			    "properties": {
			        "field1": { "type": "string" },
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			""";

		var result = await CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		result.IsCompatible.ShouldBeTrue();
	}

	[Test]
	public async Task incompatible_when_adding_required_field() {
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" },
			                "age": { "type": "integer" }
			            },
			            "required": ["age"]
			        }
			    },
			    "properties": {
			        "field1": { "type": "string" },
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
			                "name": { "type": "string" }
			            }
			        }
			    },
			    "properties": {
			        "field1": { "type": "string" },
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			""";

		var result = await CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		result.IsCompatible.ShouldBeFalse();
		result.Errors.ShouldContain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}
}
