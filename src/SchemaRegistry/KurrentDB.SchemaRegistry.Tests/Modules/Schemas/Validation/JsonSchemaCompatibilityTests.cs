// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ConvertToConstant.Local
// ReSharper disable MemberCanBeMadeStatic.Global
// ReSharper disable ArrangeTypeMemberModifiers

using Kurrent.Surge.Schema.Validation;
using KurrentDB.Common.Utils;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using NJsonSchema;

namespace Kurrent.Surge.Core.Tests.Schema.Validation;

public class JsonSchemaCompatibilityTests : SchemaApplicationTestFixture {
	static readonly NJsonSchemaCompatibilityManager CompatibilityManager = new();

	[Test]
	public async Task BackwardMode_Compatible_WhenAddingOptionalField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("email", JsonObjectType.String);

		var v2 = v1
			.Remove("email");

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenMakingRequiredFieldOptional() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("email", JsonObjectType.String)
			.AddRequired("age", JsonObjectType.Integer);

		var v2 = v1
			.MakeOptional("age");

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenWideningUnionField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddRequired("gender", JsonObjectType.Integer);

		var v2 = v1
			.WidenType("gender", JsonObjectType.Integer);

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(
			uncheckedSchema,
			referenceSchema,
			SchemaCompatibilityMode.Backward
		);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenDeletingOptionalField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("email", JsonObjectType.String);

		var v2 = v1
			.Remove("email");

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Compatible_WhenDeletingFieldWithDefaultValue() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String, "admin");

		var v2 = v1
			.Remove("role");

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenDeletingRequiredField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddRequired("role", JsonObjectType.String);

		var v2 = v1
			.Remove("role");

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenMakingOptionalFieldRequired() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String);

		var v2 = v1
			.MakeRequired("role");

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenChangingFieldType() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer);

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}

	[Test]
	public async Task BackwardMode_Incompatible_WhenAddingRequiredField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition();

		var v2 = v1
			.AddRequired("role", JsonObjectType.Integer);

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Backward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async Task ForwardMode_Compatible_WhenDeletingOptionalField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String);

		var v2 = v1
			.Remove("role");

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task ForwardMode_Compatible_WhenAddingOptionalField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition();

		var v2 = v1
			.AddOptional("role", JsonObjectType.String);

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeTrue();
	}

	[Test]
	public async Task ForwardMode_Incompatible_WhenAddingRequiredField() {
		// Arrange
		var v1 = NewJsonSchemaDefinition();

		var v2 = v1
			.AddRequired("role", JsonObjectType.String);

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async Task ForwardMode_Incompatible_WhenChangingFieldType() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer);

		var referenceSchema = v1.ToCanonicalJson();
		var uncheckedSchema = v2.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Forward);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
	}

	[Test]
	public async Task BackwardAllMode_Compatible_WithAllowedChanges() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddRequired("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.Remove("age");

		var v3 = v2
			.AddOptional("email", JsonObjectType.String);

		var uncheckedSchema = v3.ToCanonicalJson();

		var referenceSchemas = new[] { v1.ToCanonicalJson(), v2.ToCanonicalJson() };

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.BackwardAll);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task BackwardAllMode_Incompatible_WithProhibitedChanges() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer)
			.AddRequired("address", JsonObjectType.String)
			.MakeRequired("age");

		var uncheckedSchema = v2.ToCanonicalJson();

		var referenceSchemas = new[] { v1.ToCanonicalJson() };

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.BackwardAll);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Count.Should().Be(3);
		result.Errors.Should()
			.Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange)
			.And.Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty)
			.And.Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	[Test]
	public async Task ForwardAllMode_Compatible_WithAllowedChanges() {
		// Arrange

		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.Remove("role");

		var v3 = v2
			.AddOptional("email", JsonObjectType.String);

		var uncheckedSchema = v3.ToCanonicalJson();

		var referenceSchemas = new[] { v1.ToCanonicalJson(), v2.ToCanonicalJson() };

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.ForwardAll);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task ForwardAllMode_Incompatible_WithProhibitedChanges() {
		// Arrange

		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer)
			.AddRequired("email", JsonObjectType.String)
			.MakeRequired("age");

		var uncheckedSchema = v2.ToCanonicalJson();

		var referenceSchemas = new[] { v1.ToCanonicalJson() };

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.ForwardAll);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Count.Should().Be(2);
		result.Errors.Should()
			.Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange)
			.And.Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
	}

	[Test]
	public async Task FullMode_Compatible_WithAddingOptionalFields() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.AddOptional("email", JsonObjectType.String);

		var uncheckedSchema = v2.ToCanonicalJson();
		var referenceSchema = v1.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task FullMode_Compatible_WithDeletingOptionalFields() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.Remove("role");

		var uncheckedSchema = v2.ToCanonicalJson();
		var referenceSchema = v1.ToCanonicalJson();

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchema, SchemaCompatibilityMode.Full);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task FullMode_Incompatible_WhenChangingFieldType() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer);

		var uncheckedSchema = v2.ToCanonicalJson();
		var referenceSchema = v1.ToCanonicalJson();

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
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.AddRequired("email", JsonObjectType.String);

		var uncheckedSchema = v2.ToCanonicalJson();
		var referenceSchema = v1.ToCanonicalJson();


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
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.MakeRequired("role");

		var uncheckedSchema = v2.ToCanonicalJson();
		var referenceSchema = v1.ToCanonicalJson();

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
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.Remove("age");

		var v3 = v2
			.AddOptional("email", JsonObjectType.String);

		var uncheckedSchema = v3.ToCanonicalJson();

		var referenceSchemas = new[] { v1.ToCanonicalJson(), v2.ToCanonicalJson() };

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.FullAll);

		// Assert
		result.IsCompatible.Should().BeTrue();
		result.Errors.Should().BeEmpty();
	}

	[Test]
	public async Task FullAllMode_Incompatible_WithProhibitedChanges() {
		// Arrange
		var v1 = NewJsonSchemaDefinition()
			.AddOptional("role", JsonObjectType.String)
			.AddOptional("age", JsonObjectType.Integer);

		var v2 = v1
			.ChangeType("role", JsonObjectType.Integer)
			.AddRequired("email", JsonObjectType.String)
			.MakeRequired("age");

		var uncheckedSchema = v2.ToCanonicalJson();

		var referenceSchemas = new[] { v1.ToCanonicalJson() };

		// Act
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.FullAll);

		// Assert
		result.IsCompatible.Should().BeFalse();
		result.Errors.Count.Should().Be(3);
		result.Errors.Should()
			.Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange)
			.And.Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty)
			.And.Contain(e => e.Kind == SchemaCompatibilityErrorKind.OptionalToRequired);
	}

	#region references

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
		var result = await CompatibilityManager.CheckCompatibility(uncheckedSchema, referenceSchemas, SchemaCompatibilityMode.BackwardAll);

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

	#endregion
}
