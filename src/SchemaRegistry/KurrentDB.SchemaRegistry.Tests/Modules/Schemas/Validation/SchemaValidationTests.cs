using Bogus;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Schema.Serializers.Json;
using Kurrent.Surge.Schema.Validation;
using NJsonSchema.Validation;

using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Kurrent.Surge.Core.Tests.Schema.Validation;

public class SchemaValidationTests {
    [Test]
    public void message_schema_must_be_valid() {
        var expectedResult = SchemaValidationResult.Success();

        var sample = new Faker<PowerConsumption>()
            .RuleFor(x => x.Timestamp, (f, x) => x.Timestamp = Timestamp.FromDateTimeOffset(f.Date.SoonOffset()))
            .RuleFor(x => x.Value    , (f, x) => x.Value = f.Random.Double())
            .RuleFor(x => x.DeviceId , (f, x) => x.DeviceId = f.Random.Guid().ToString())
            .RuleFor(x => x.Unit     , "celsius")
            .Generate();

        var schema       = NJsonSchemaAgent.Instance.ExportSchema<PowerConsumption>();
        var content      = JsonSerializer.Serialize(sample, SystemJsonSchemaSerializerOptions.Default);
        var actualResult = schema.Validate(content);

        actualResult.Should().BeEquivalentTo(expectedResult);
    }

    [Test]
    public void message_schema_must_be_valid_from_bytes() {
        var expectedResult = SchemaValidationResult.Success();

        var sample = new Faker<PowerConsumption>()
            .RuleFor(x => x.Timestamp, (f, x) => x.Timestamp = Timestamp.FromDateTimeOffset(f.Date.SoonOffset()))
            .RuleFor(x => x.Value    , (f, x) => x.Value = f.Random.Double())
            .RuleFor(x => x.DeviceId , (f, x) => x.DeviceId = f.Random.Guid().ToString())
            .RuleFor(x => x.Unit     , "celsius")
            .Generate();

        var schema       = NJsonSchemaAgent.Instance.ExportSchema<PowerConsumption>();
        var content      = JsonSerializer.SerializeToUtf8Bytes(sample, SystemJsonSchemaSerializerOptions.Default).AsSpan();
        var actualResult = schema.Validate(content);

        actualResult.Should().BeEquivalentTo(expectedResult);
    }

    [Test]
    public void message_schema_must_be_invalid() {
        var sample = new Faker<DeviceTelemetry>()
            .RuleFor(x => x.Timestamp, (f, x) => x.Timestamp = Timestamp.FromDateTimeOffset(f.Date.SoonOffset()))
            .RuleFor(x => x.DeviceId , (f, x) => x.DeviceId = f.Random.Guid().ToString())
            .RuleFor(x => x.DataType , (f, x) => x.DataType = f.System.CommonFileType())
            .Generate();

        var expectedResult = SchemaValidationResult.Failure(
            new SchemaValidationError {
                Property     = "dataType",
                Path         = "#/dataType",
                ErrorMessage = ValidationErrorKind.NoAdditionalPropertiesAllowed.Humanize(),
                LineInfo     = new(1, 62)
            }
           // , new() {
           //      Property     = "data",
           //      Path         = "#/data",
           //      Kind         = ValidationErrorKind.NoAdditionalPropertiesAllowed,
           //      ErrorMessage = ValidationErrorKind.NoAdditionalPropertiesAllowed.Humanize(),
           //      LineInfo     = new(1, 77)
           //  }
        );

        var schema       = NJsonSchemaAgent.Instance.ExportSchema<PowerConsumption>();
        var content      = JsonSerializer.Serialize(sample, SystemJsonSchemaSerializerOptions.Default);
        var actualResult = schema.Validate(content);

        // for some reason the line info changes. bug on the NJsonSchema library?
        // actualResult.Should().BeEquivalentTo(
        //     expectedResult,
        //     options => options.For(x => x.Errors).Exclude(x => x.LineInfo)
        // );

        actualResult.Should().BeEquivalentTo(expectedResult);
    }
}
