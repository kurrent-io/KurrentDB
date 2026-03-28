// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Schema.Validation;
using static Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.JsonSchemaCompatibilityFixture;

namespace Kurrent.Surge.Core.Tests.Schema.Validation.JsonSchemaCompatibility.References;

[Category("JsonSchemaCompatibility")]
public class WhenBackwardAllMode {
	[Test]
	public async Task compatible_with_multiple_schemas() {
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" },
			                "age": { "type": "integer" },
			                "email": { "type": "string" }
			            }
			        }
			    },
			    "properties": {
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			""";

		var referenceSchemas = new[] {
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
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			""",
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
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			"""
		};

		var result = await CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.BackwardAll);

		result.IsCompatible.ShouldBeTrue();
	}
}
