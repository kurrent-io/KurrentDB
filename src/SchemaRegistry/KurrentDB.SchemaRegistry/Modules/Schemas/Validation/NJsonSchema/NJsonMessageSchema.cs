using System.Text.Json.Nodes;
using Kurrent.Surge.Schema.Serializers.Json;
using NJsonSchema;
using NJsonSchema.Validation;
using SchemaType = NJsonSchema.SchemaType;

namespace Kurrent.Surge.Schema.Validation;

[PublicAPI]
public class NJsonMessageSchema(string schemaDefinition, JsonSchema schema, JsonSchemaValidatorSettings validatorSettings, SystemJsonSerializer serializer) : MessageSchema(schemaDefinition) {
    internal JsonSchema Schema { get; } = schema;

    JsonSchemaValidatorSettings ValidatorSettings { get; } = validatorSettings;
    SystemJsonSerializer        Serializer        { get; } = serializer;

    public override SchemaValidationResult Validate(string data) {
        try {
            var errors = Schema.Validate(data, SchemaType.JsonSchema, ValidatorSettings);
            return SchemaValidationResult.Failure(errors.Map());
        }
        catch (Exception ex) {
            throw new SchemaValidationException(ex);
        }
    }

    public override SchemaValidationResult Validate(ReadOnlySpan<byte> data) {
        if (data.IsEmpty)
            throw new InvalidOperationException("Data cannot be empty.");

        try {
            var json = ((JsonNode)Serializer.Deserialize(data.ToArray(), Type.Missing.GetType())!).ToJsonString();
            var errors = Schema.Validate(json, SchemaType.JsonSchema, ValidatorSettings);
            return SchemaValidationResult.Failure(errors.Map());
        }
        catch (Exception ex) {
            throw new SchemaValidationException(ex);
        }
    }

    public override SchemaValidationResult Validate<T>(T data) where T : class {
        Ensure.NotNull(data);
        return Validate(Serializer.Serialize(data).Span);
    }

    public override SchemaCompatibilityResult IsCompatible(MessageSchema other, SchemaCompatibilityMode mode) =>
        NJsonSchemaCompatibilityManager.CheckCompatibility(Schema, ((NJsonMessageSchema)other).Schema, mode);
}