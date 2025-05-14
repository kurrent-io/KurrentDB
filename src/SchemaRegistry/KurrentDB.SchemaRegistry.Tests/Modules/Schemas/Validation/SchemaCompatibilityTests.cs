using Kurrent.Surge.Schema.Validation;

namespace Kurrent.Surge.Core.Tests.Schema.Validation;

public class SchemaCompatibilityTests {
    static readonly ISchemaCompatibilityManager CompatibilityManager = new NJsonSchemaCompatibilityManager();

    [Test]
    public async Task CheckCompatibility_NoneMode_ReturnsCompatible() {
        // Arrange
        var schema1 = "{}";
        var schema2 = "{}";

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.None);

        // Assert
        result.IsCompatible.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task CheckCompatibility_BackwardMode_ReturnsIncompatibleForMissingRequiredField() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "string" }
                          },
                          "required": ["field1"]
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "field2": { "type": "string" }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Backward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
    }

    [Test]
    public async Task CheckCompatibility_BackwardMode_ReturnsIncompatibleForMissingRequiredFieldInReference() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field2": {
                                          "type": "string"
                                      }
                                  }
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Backward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
    }

    [Test]
    public async Task CheckCompatibility_BackwardMode_ReturnsCompatibleForInlinedObjectReference() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Backward);

        // Assert
        result.IsCompatible.Should().BeTrue();
    }

    [Test]
    public async Task CheckCompatibility_ForwardMode_ReturnsIncompatibleForNewRequiredField() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "string" }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
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
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Forward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
    }

    [Test]
    public async Task CheckCompatibility_ForwardMode_ReturnsIncompatibleForNewRequiredFieldInReference() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      }
                                  }
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field2"]
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Forward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
    }

    [Test]
    public async Task CheckCompatibility_ForwardMode_ReturnsCompatibleForInlinedObjectReference() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Forward);

        // Assert
        result.IsCompatible.Should().BeTrue();
    }

    [Test]
    public async Task CheckCompatibility_FullMode_ReturnsIncompatibleForBothBackwardAndForwardIssues() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "string" }
                          },
                          "required": ["field1"]
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "field2": { "type": "string" }
                          },
                          "required": ["field2"]
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Full);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
    }

    [Test]
    public async Task CheckCompatibility_FullMode_ReturnsCompatibleForInlinedObjectReference() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      },
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Full);

        // Assert
        result.IsCompatible.Should().BeTrue();
    }

    [Test]
    public async Task CheckCompatibility_FullMode_ReturnsIncompatibleForBothBackwardAndForwardIssuesInReference() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field1": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field1"]
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "ref1": { "$ref": "#/definitions/Definition1" }
                          },
                          "definitions": {
                              "Definition1": {
                                  "type": "object",
                                  "properties": {
                                      "field2": {
                                          "type": "string"
                                      }
                                  },
                                  "required": ["field2"]
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Full);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.MissingRequiredProperty);
    }

    [Test]
    public async Task CheckCompatibility_UnspecifiedMode_ThrowsArgumentException() {
        // Arrange
        var schema1 = "{}";
        var schema2 = "{}";

        // Act
        var check = async () => await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Unspecified);

        // Assert
        await check.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task CheckCompatibility_BackwardMode_ReturnsIncompatibleForTypeChange() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "string" }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "integer" }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Backward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.IncompatibleTypeChange);
    }

    [Test]
    public async Task CheckCompatibility_ForwardMode_ReturnsIncompatibleForOptionalToRequiredField() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "string" }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
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
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Forward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.NewRequiredProperty);
    }

    [Test]
    public async Task CheckCompatibility_FullMode_ReturnsIncompatibleForNestedObjectChanges() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": {
                                  "type": "object",
                                  "properties": {
                                      "nestedField1": { "type": "string" }
                                  },
                                  "required": ["nestedField1"]
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": {
                                  "type": "object",
                                  "properties": {
                                      "nestedField2": { "type": "string" }
                                  },
                                  "required": ["nestedField2"]
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Full);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath.Contains("nestedField1"));
    }

    [Test]
    public async Task CheckCompatibility_BackwardMode_ReturnsIncompatibleForArrayTypeChange() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": {
                                  "type": "array",
                                  "items": { "type": "string" }
                              }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": {
                                  "type": "array",
                                  "items": { "type": "integer" }
                              }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Backward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.ArrayTypeIncompatibility);
    }

    [Test]
    public async Task CheckCompatibility_ForwardMode_ReturnsIncompatibleForRemovedField() {
        // Arrange

        // lang=json
        var schema1 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "string" },
                              "field2": { "type": "string" }
                          }
                      }
                      """;

        // lang=json
        var schema2 = """
                      {
                          "type": "object",
                          "properties": {
                              "field1": { "type": "string" }
                          }
                      }
                      """;

        // Act
        var result = await CompatibilityManager.CheckCompatibility(schema1, schema2, SchemaCompatibilityMode.Forward);

        // Assert
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Kind == SchemaCompatibilityErrorKind.RemovedProperty);
    }
}