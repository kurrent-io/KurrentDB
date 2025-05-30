// ReSharper disable ConvertToConstant.Local
// ReSharper disable MemberCanBeMadeStatic.Global
// ReSharper disable ArrangeTypeMemberModifiers

using Kurrent.Surge.Schema.Validation;

namespace Kurrent.Surge.Core.Tests.Schema.Validation;

public class SchemaCompatibilityTests {
	static readonly NJsonSchemaCompatibilityManager CompatibilityManager = new();

	[Test]
	public async Task BackwardMode_Compatible_WhenAddingOptionalField() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenMakingRequiredFieldOptional() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    },
			    "required": ["field2"]
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenWideningUnionField() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": ["string", "int"] }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			    },
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenDeletingOptionalField() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" },
			    },
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenDeletingFieldWithDefaultValue() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "int", "default": 1 },
			    },
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenDeletingRequiredField() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    },
			    "required": ["field2"]
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenMakingOptionalFieldRequired() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    },
			    "required": ["field1"]
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    },
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenChangingFieldType() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "integer" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenAddingRequiredField() {
		// Arrange
		var uncheckedSchema =
			"""
			{
				"type": "object",
				"properties": {
					"field1": { "type": "string" },
					"field2": { "type": "string" }
				},
				"required": ["field2"]
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async Task ForwardMode_Compatible_WhenDeletingOptionalField() {
		// Arrange

		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task ForwardMode_Compatible_WhenAddingOptionalField() {
		// Arrange

		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task ForwardMode_Incompatible_WhenAddingRequiredField() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    },
			    "required": ["field2"]
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async Task ForwardMode_Incompatible_WhenChangingFieldType() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "integer" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}

	[Test]
	public async Task BackwardAllMode_Compatible_WithAllowedChanges() {
		// Arrange

		// Added optional fields
		// Removed optional fields
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field3": { "type": "boolean" }
			    }
			}
			""";

		var referenceSchemas = new[] {
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""",
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "integer" }
			    },
			    "required": ["field1"]
			}
			"""
		};

		// Act
		var result = await CompatibilityManager.CheckCompatibilityAll(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.BackwardAll);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task BackwardAllMode_Incompatible_WithProhibitedChanges() {
		// Arrange

		// Changed field type
		// Added required fields
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "integer" },
			        "field2": { "type": "string" },
			        "field3": { "type": "boolean" }
			    },
			    "required": ["field2", "field3"]
			}
			""";

		var referenceSchemas = new[] {
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			"""
		};

		// Act
		var result = await CompatibilityManager.CheckCompatibilityAll(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.BackwardAll);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
		result.Errors.Count.Should().Be(3);
	}

	[Test]
	public async Task ForwardAllMode_Compatible_WithAllowedChanges() {
		// Arrange

		// Deleted optional fields
		// Added optional fields
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field3": { "type": "boolean" }
			    }
			}
			""";

		var referenceSchemas = new[] {
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""",
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "integer" }
			    }
			}
			"""
		};

		// Act
		var result = await CompatibilityManager.CheckCompatibilityAll(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.ForwardAll);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task ForwardAllMode_Incompatible_WithProhibitedChanges() {
		// Arrange

		// Changed field type
		// Added required fields
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "integer" },
			        "field2": { "type": "string" },
			        "field3": { "type": "boolean" }
			    },
			    "required": ["field2", "field3"]
			}
			""";

		var referenceSchemas = new[] {
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			"""
		};

		// Act
		var result = await CompatibilityManager.CheckCompatibilityAll(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.ForwardAll);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		result.Errors.Count.Should().Be(2);
	}

	[Test]
	public async Task FullMode_Compatible_WithAddingOptionalFields() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" },
			        "field3": { "type": "boolean" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task FullMode_Compatible_WithDeletingOptionalFields() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task FullMode_Incompatible_WithChangingFieldType() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "integer" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		result.Errors.Count.Should().Be(1);
	}

	[Test]
	public async Task FullMode_Incompatible_WithAddingRequiredField() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" },
			        "field3": { "type": "boolean" }
			    },
			    "required": ["field3"]
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		result.Errors.Count.Should().Be(1);
	}

	[Test]
	public async Task FullMode_Incompatible_WithMakingOptionalFieldRequired() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    },
			    "required": ["field2"]
			}
			""";

		var referenceSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
		result.Errors.Count.Should().Be(1);
	}

	[Test]
	public async Task FullAllMode_Compatible_WithAllowedChanges() {
		// Arrange

		// Added optional fields
		// Removed optional fields
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field3": { "type": "boolean" }
			    }
			}
			""";

		var referenceSchemas = new[] {
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" }
			    }
			}
			""",
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "integer" }
			    }
			}
			"""
		};

		// Act
		var result = await CompatibilityManager.CheckCompatibilityAll(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.FullAll);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task FullAllMode_Incompatible_WithProhibitedChanges() {
		// Arrange

		// Changed field type
		// Added required fields
		// Made optional field required
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "integer" },
			        "field2": { "type": "string" },
			        "field3": { "type": "boolean" }
			    },
			    "required": ["field2", "field3"]
			}
			""";

		var referenceSchemas = new[] {
			"""
			{
			    "type": "object",
			    "properties": {
			        "field1": { "type": "string" },
			        "field2": { "type": "string" }
			    }
			}
			"""
		};

		// Act
		var result = await CompatibilityManager.CheckCompatibilityAll(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.FullAll);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
		result.Errors.Count.Should().Be(3);
	}

	[Test]
	public async Task BackwardMode_WithReferences_Compatible_WhenAddingOptionalField() {
		// Arrange
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

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_WithReferences_Incompatible_WhenRemovingRequiredField() {
		// Arrange
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

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
	}

	[Test]
	public async Task ForwardMode_WithReferences_Compatible_WhenRemovingOptionalField() {
		// Arrange
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

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task ForwardMode_WithReferences_Incompatible_WhenAddingRequiredField() {
		// Arrange
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

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async Task FullMode_WithReferences_Compatible_WithAllowedChanges() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" },
			                "email": { "type": "string" }
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

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task FullMode_WithReferences_Incompatible_WithDisallowedChanges() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "integer" },
			                "email": { "type": "string" }
			            },
			            "required": ["email"]
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

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async Task NestedReferences_AreCorrectlyResolved() {
		// Arrange
		var uncheckedSchema =
			"""
			{
			    "type": "object",
			    "definitions": {
			        "address": {
			            "type": "object",
			            "properties": {
			                "street": { "type": "string" },
			                "city": { "type": "string" }
			            }
			        },
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" },
			                "address": { "$ref": "#/definitions/address" }
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
			        "address": {
			            "type": "object",
			            "properties": {
			                "street": { "type": "string" },
			                "city": { "type": "string" },
			                "zipCode": { "type": "string" },
			                "country": { "type": "string" }
			            },
			            "required": ["zipCode"]
			        },
			        "person": {
			            "type": "object",
			            "properties": {
			                "name": { "type": "string" },
			                "address": { "$ref": "#/definitions/address" }
			            }
			        }
			    },
			    "properties": {
			        "person": { "$ref": "#/definitions/person" }
			    }
			}
			""";

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
		result.Errors.Should().Contain(e => e.PropertyPath == "#/person/address/zipCode");
	}

	[Test]
	public async Task BackwardAllMode_WithReferences_Compatible_WithMultipleSchemas() {
		// Arrange
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

		// Act
		var result = await CompatibilityManager.CheckCompatibilityAll(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.BackwardAll);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task CircularReferences_AreHandledCorrectly() {
		// Arrange
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

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
		result.Errors.Should().Contain(e => e.PropertyPath == "#/person/age");
	}
}
