// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Testing.TUnit;
using NJsonSchema;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Integration;

public class UpdateSchemaIntegrationTests : SchemaApplicationTestFixture {
    [Test]
    public async ValueTask updates_schema_successfully(CancellationToken cancellationToken) {
        // Arrange
        var prefix       = NewPrefix();
        var schemaName   = NewSchemaName(prefix);
        var originalTags = new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() };
        var newTags      = new Dictionary<string, string> { [Faker.Lorem.Word()] = Faker.Lorem.Word() };

        await Fixture.CreateSchema(
            schemaName,
            new SchemaDetails {
                Tags          = { originalTags },
                Compatibility = CompatibilityMode.Forward,
                DataFormat    = SchemaFormat.Json
            },
            cancellationToken
        );

        // Act
        await Fixture.UpdateSchema(
            schemaName,
            new SchemaDetails {
                Tags          = { newTags },
                Compatibility = CompatibilityMode.Forward,
                DataFormat    = SchemaFormat.Json
            },
            new FieldMask { Paths = { "Details.Tags" } },
            cancellationToken
        );

        // Assert
        var listSchemasResult = await Fixture.ListRegisteredSchemas(prefix, cancellationToken);

        listSchemasResult
            .Schemas.ShouldHaveSingleItem()
            .Tags.ShouldBe(newTags);
    }

    [Test]
    public async ValueTask throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        await Fixture.CreateSchema(schemaName, cancellationToken);
        await Fixture.DeleteSchema(schemaName, cancellationToken);

        // Act
        var action = async () => await Fixture.UpdateSchema(
            schemaName,
            new SchemaDetails {
                Description   = Faker.Lorem.Sentence(),
                Compatibility = CompatibilityMode.Backward,
                DataFormat    = SchemaFormat.Json
            },
            new FieldMask { Paths = { "Details.Description" } },
            cancellationToken
        );

        // Assert
        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.NotFound);
        rex.Message.ShouldContain($"Schema schemas/{schemaName} not found");
    }

    [Test]
    public async ValueTask throws_exception_when_update_mask_contains_unknown_field(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();

        await Fixture.CreateSchema(schemaName, cancellationToken);

        // Act
        var action = async () => await Fixture.UpdateSchema(
            schemaName,
            new SchemaDetails {
                Description   = Faker.Lorem.Sentence(),
                Compatibility = CompatibilityMode.Backward,
                DataFormat    = SchemaFormat.Json
            },
            new FieldMask { Paths = { "Details.UnknownField" } },
            cancellationToken
        );

        // Assert
        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain("Unknown field Details.UnknownField in update mask");
    }

    [Test]
    [NotModifiableTestCases]
    public async ValueTask throws_exception_when_trying_to_update_non_modifiable_fields(
        SchemaDetails schemaDetails, string maskPath, string errorMessage, CancellationToken cancellationToken
    ) {
        // Arrange
        var schemaName = NewSchemaName();
        await Fixture.CreateSchema(schemaName, cancellationToken);

        // Act
        var action = async () => await Fixture.UpdateSchema(
            schemaName,
            schemaDetails,
            new FieldMask { Paths = { maskPath } },
            cancellationToken
        );

        // Assert
        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain(errorMessage);
    }

    [Test]
    [UnchangedFieldsTestCases]
    public async ValueTask throws_exception_when_fields_has_not_changed(
        SchemaDetails schemaDetails, string maskPath, string errorMessage,
        CancellationToken cancellationToken
    ) {
        // Arrange
        var schemaName = NewSchemaName();

        var details = new SchemaDetails {
            Description   = schemaDetails.Description,
            Compatibility = CompatibilityMode.None,
            DataFormat    = SchemaFormat.Json,
            Tags          = { new Dictionary<string, string>(schemaDetails.Tags) }
        };

        await Fixture.CreateSchema(schemaName, details, cancellationToken);

        // Act
        var action = async () => await Fixture.UpdateSchema(
            schemaName,
            schemaDetails,
            new FieldMask { Paths = { maskPath } },
            cancellationToken
        );

        // Assert
        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain(errorMessage);
    }

    public class NotModifiableTestCases : TestCaseGenerator<SchemaDetails, string, string> {
        protected override IEnumerable<(SchemaDetails, string, string)> Data() {
            yield return (
                new SchemaDetails {
                    Compatibility = CompatibilityMode.Forward,
                    DataFormat    = SchemaFormat.Protobuf
                },
                "Details.DataFormat",
                "DataFormat is not modifiable"
            );
        }
    }

    public class UnchangedFieldsTestCases : TestCaseGenerator<SchemaDetails, string, string> {
        protected override IEnumerable<(SchemaDetails, string, string)> Data() {
            yield return (
                new SchemaDetails {
                    Description   = "Unchanged description",
                    Compatibility = CompatibilityMode.Backward,
                    DataFormat    = SchemaFormat.Json
                },
                "Details.Description",
                "Description has not changed"
            );

            yield return (
                new SchemaDetails {
                    Tags          = { ["env"] = "test" },
                    Compatibility = CompatibilityMode.Backward,
                    DataFormat    = SchemaFormat.Json
                },
                "Details.Tags",
                "Tags have not changed"
            );

        }
    }

    [Test]
    public async ValueTask updates_compatibility_mode_successfully(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = NewSchemaName();
        var v1 = NewJsonSchema();
        var v2 = v1.AddOptional("email", JsonObjectType.String);

        await Fixture.CreateSchema(schemaName, v1, CompatibilityMode.None, SchemaFormat.Json, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);

        // Act
        await Fixture.UpdateSchema(
            schemaName,
            new SchemaDetails { Compatibility = CompatibilityMode.Backward, DataFormat = SchemaFormat.Json },
            new FieldMask { Paths = { "Details.Compatibility" } },
            cancellationToken
        );

        // Assert
        var schema = await Fixture.GetSchema(schemaName, cancellationToken);
        schema.Schema.Details.Compatibility.ShouldBe(CompatibilityMode.Backward);
    }

    [Test]
    [Arguments(CompatibilityMode.Backward)]
    [Arguments(CompatibilityMode.Forward)]
    [Arguments(CompatibilityMode.Full)]
    [Arguments(CompatibilityMode.BackwardAll)]
    [Arguments(CompatibilityMode.ForwardAll)]
    [Arguments(CompatibilityMode.FullAll)]
    public async ValueTask changes_to_compatibility_mode_with_compatible_versions(
        CompatibilityMode newMode, CancellationToken cancellationToken
    ) {
        // Arrange — v2 adds an optional field, which is compatible in all directions
        var schemaName = NewSchemaName();
        var v1 = NewJsonSchema();
        var v2 = v1.AddOptional("email", JsonObjectType.String);

        await Fixture.CreateSchema(schemaName, v1, CompatibilityMode.None, SchemaFormat.Json, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);

        // Act
        await Fixture.UpdateSchema(
            schemaName,
            new SchemaDetails { Compatibility = newMode, DataFormat = SchemaFormat.Json },
            new FieldMask { Paths = { "Details.Compatibility" } },
            cancellationToken
        );

        // Assert
        var schema = await Fixture.GetSchema(schemaName, cancellationToken);
        schema.Schema.Details.Compatibility.ShouldBe(newMode);
    }

    [Test]
    [Arguments(CompatibilityMode.Backward, "Cannot change to Backward compatibility mode")]
    [Arguments(CompatibilityMode.Forward, "Cannot change to Forward compatibility mode")]
    [Arguments(CompatibilityMode.Full, "Cannot change to Full compatibility mode")]
    [Arguments(CompatibilityMode.BackwardAll, "Cannot change to BackwardAll compatibility mode")]
    [Arguments(CompatibilityMode.ForwardAll, "Cannot change to ForwardAll compatibility mode")]
    [Arguments(CompatibilityMode.FullAll, "Cannot change to FullAll compatibility mode")]
    public async ValueTask throws_when_changing_to_compatibility_mode_with_incompatible_versions(
        CompatibilityMode newMode, string expectedError, CancellationToken cancellationToken
    ) {
        // Arrange — v2 adds a required field, which breaks compatibility in all directions
        var schemaName = NewSchemaName();
        var v1 = NewJsonSchema();
        var v2 = v1.AddRequired("age", JsonObjectType.Integer);

        await Fixture.CreateSchema(schemaName, v1, CompatibilityMode.None, SchemaFormat.Json, cancellationToken);
        await Fixture.RegisterSchemaVersion(schemaName, v2, cancellationToken);

        // Act
        var action = async () => await Fixture.UpdateSchema(
            schemaName,
            new SchemaDetails { Compatibility = newMode, DataFormat = SchemaFormat.Json },
            new FieldMask { Paths = { "Details.Compatibility" } },
            cancellationToken
        );

        // Assert
        var rex = await action.ShouldThrowAsync<RpcException>();
        rex.Status.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
        rex.Message.ShouldContain(expectedError);
    }
}
