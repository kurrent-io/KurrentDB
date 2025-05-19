using System.Text.Json;
using Bogus;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Schema.Serializers.Json;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using Kurrent.Surge.Schema.Validation;

namespace Kurrent.Surge.Core.Tests.Schema.Validation;

public class SchemaExportTests {
    [Test, Skip("temporary")]
    public void exports_from_type() {
        // lang=json
        var expectedDefinition = """
            {
              "$schema": "http://json-schema.org/draft-04/schema#",
              "title": "PowerConsumption",
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "DeviceId": {
                  "type": [
                    "null",
                    "string"
                  ]
                },
                "Unit": {
                  "type": [
                    "null",
                    "string"
                  ]
                },
                "Value": {
                  "type": [
                    "null",
                    "number"
                  ],
                  "format": "double"
                },
                "Timestamp": {
                  "oneOf": [
                    {
                      "type": "null"
                    },
                    {
                      "$ref": "#/definitions/Timestamp"
                    }
                  ]
                }
              },
              "definitions": {
                "Timestamp": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "Seconds": {
                      "type": "integer",
                      "format": "int64"
                    },
                    "Nanos": {
                      "type": "integer",
                      "format": "int32"
                    }
                  }
                }
              }
            }
            """;

        var extract = () => NJsonSchemaAgent.Instance.ExportSchema<PowerConsumption>();
        var schema  = extract.Should().NotThrow().Subject;

        schema.Definition.Should().BeEquivalentTo(expectedDefinition);
    }

    [Test]
    public void parses_schema() {
        var schema       = NJsonSchemaAgent.Instance.ExportSchema<PowerConsumption>();
        var loadedSchema = NJsonSchemaAgent.Instance.ParseSchema(schema.Definition);
        loadedSchema.Definition.Should().BeEquivalentTo(schema.Definition);
    }

    [Test]
    public void generates_schema() {
      var expectedResult = SchemaValidationResult.Success();

      var sample = new Faker<PowerConsumption>()
        .RuleFor(x => x.Timestamp, (f, x) => x.Timestamp = Timestamp.FromDateTimeOffset(f.Date.SoonOffset()))
        .RuleFor(x => x.Value    , (f, x) => x.Value = f.Random.Double())
        .RuleFor(x => x.DeviceId , (f, x) => x.DeviceId = f.Random.Guid().ToString())
        .RuleFor(x => x.Unit     , "celsius")
        .Generate();

      var content         = JsonSerializer.Serialize(sample, SystemJsonSchemaSerializerOptions.Default);
      var generatedSchema = NJsonSchemaExporter.Instance.GetJsonSchemaFromData(content, nameof(PowerConsumption));
      var schema          = NJsonSchemaAgent.Instance.ExportSchema<PowerConsumption>();

      var oldJson = generatedSchema.ToJson();

      var anotherResult = generatedSchema.Validate(content);
      var actualResult  = schema.Validate(content);

      actualResult.Should().BeEquivalentTo(expectedResult);
    }
}

record SchemaTestSubject {
    public string                     String     { get; set; } = null!;
    public int                        Int        { get; set; }
    public double                     Double     { get; set; }
    public bool                       Bool       { get; set; }
    public DateTime                   DateTime   { get; set; }
    public Guid                       Guid       { get; set; }
    public Uri                        Uri        { get; set; } = null!;
    public byte[]                     Bytes      { get; set; } = null!;
    public string[]                   Array      { get; set; } = null!;
    public List<string>               List       { get; set; } = null!;
    public Dictionary<string, string> Dictionary { get; set; } = null!;
    public SchemaTestSubject          Nested     { get; set; } = null!;
}
