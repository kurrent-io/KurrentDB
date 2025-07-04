using System.Text.Json;
using System.Text.RegularExpressions;
using Humanizer;
using NJsonSchema;

namespace Kurrent.Surge.Schema.Validation;

public partial class NJsonSchemaExporter {
    public static readonly NJsonSchemaExporter Instance = new();

    static readonly Regex DatePattern     = DatePatternRegex();
    static readonly Regex DateTimePattern = DateTimePatternRegex();
    static readonly Regex TimePattern     = TimePatternRegex();

    /// <summary>Generates the JSON Schema for the given JSON data.</summary>
    /// <param name="data">The JSON data.</param>
    /// <param name="title">The title of the schema. If not provided, the title will not be set.</param>
    /// <returns>The JSON Schema.</returns>
    public JsonSchema GetJsonSchemaFromData(string data, string? title = null)  {
        using var document = JsonDocument.Parse(
            data,
            new JsonDocumentOptions {
                AllowTrailingCommas = true,
                CommentHandling     = JsonCommentHandling.Skip
            }
        );

        var element = document.RootElement;

        var schema = new JsonSchema();

        if (title is not null)
            schema.Title = title;

        Generate(element, schema, schema, "Anonymous");

        return schema;
    }

    void Generate(JsonElement element, JsonSchema schema, JsonSchema rootSchema, string typeNameHint) {
        if (schema != rootSchema && element.ValueKind == JsonValueKind.Object) {
            JsonSchema? referencedSchema = null;

            var properties = element.EnumerateObject().Select(p => p.Name).ToList();

            if (properties.Count != 0)
                referencedSchema = rootSchema.Definitions
                    .Select(t => t.Value)
                    .FirstOrDefault(
                        s => s.Type == JsonObjectType.Object
                          && properties.All(p => s.Properties.ContainsKey(p))
                    );

            if (referencedSchema is null) {
                referencedSchema = new JsonSchema();
                AddSchemaDefinition(rootSchema, referencedSchema, typeNameHint);
            }

            schema.Reference = referencedSchema;
            GenerateWithoutReference(element, referencedSchema, rootSchema, typeNameHint);
            return;
        }

        GenerateWithoutReference(element, schema, rootSchema, typeNameHint);
    }

    void GenerateWithoutReference(JsonElement element, JsonSchema schema, JsonSchema rootSchema, string typeNameHint) {
        switch (element.ValueKind) {
            case JsonValueKind.Object:
                GenerateObject(element, schema, rootSchema);
                break;

            case JsonValueKind.Array:
                GenerateArray(element, schema, rootSchema, typeNameHint);
                break;

            case JsonValueKind.String:
                NJsonSchemaExporter.GenerateString(element, schema);
                break;

            case JsonValueKind.Number:
                schema.Type = element.TryGetInt64(out _)
                    ? JsonObjectType.Integer
                    : JsonObjectType.Number;

                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                schema.Type = JsonObjectType.Boolean;
                break;

            case JsonValueKind.Null:
                // Handle null values - might want to make this configurable
                break;
        }
    }

    static void GenerateString(JsonElement element, JsonSchema schema) {
        schema.Type = JsonObjectType.String;
        var value = element.GetString();

        if (string.IsNullOrEmpty(value))
            return;

        if (Guid.TryParse(value, out _))
            schema.Format = JsonFormatStrings.Guid;
        else if (Uri.TryCreate(value, UriKind.Absolute, out _))
            schema.Format = JsonFormatStrings.Uri;
        else if (DatePattern.IsMatch(value))
            schema.Format = JsonFormatStrings.Date;
        else if (DateTimePattern.IsMatch(value))
            schema.Format = JsonFormatStrings.DateTime;
        else if (TimePattern.IsMatch(value))
            schema.Format = JsonFormatStrings.Duration;
    }

    void GenerateObject(JsonElement element, JsonSchema schema, JsonSchema rootSchema) {
        schema.Type = JsonObjectType.Object;
        foreach (var property in element.EnumerateObject()) {
            var propertySchema = new JsonSchemaProperty();

            var propertyName = property.Value.ValueKind == JsonValueKind.Array
                ? ConversionUtilities.Singularize(property.Name.Pascalize())
                : property.Name.Pascalize();

            // var typeNameHint = property.Name.Pascalize(); //ConversionUtilities.ConvertToUpperCamelCase(propertyName, true);

            var typeNameHint = ConversionUtilities.ConvertToUpperCamelCase(propertyName, true);

            Generate(property.Value, propertySchema, rootSchema, typeNameHint);

            schema.Properties[propertyName] = propertySchema;
        }
    }

    void GenerateArray(JsonElement element, JsonSchema schema, JsonSchema rootSchema, string typeNameHint) {
        schema.Type = JsonObjectType.Array;

        var itemSchemas = element.EnumerateArray().Select(
            item => {
                var itemSchema = new JsonSchema();
                GenerateWithoutReference(item, itemSchema, rootSchema, typeNameHint);
                return itemSchema;
            }
        ).ToList();

        if (itemSchemas.Count == 0)
            schema.Item = new JsonSchema();
        else if (itemSchemas.GroupBy(s => s.Type).Count() == 1)
            MergeAndAssignItemSchemas(rootSchema, schema, itemSchemas, typeNameHint);
        else
            schema.Item = itemSchemas.First();
    }

    static void MergeAndAssignItemSchemas(JsonSchema rootSchema, JsonSchema schema, List<JsonSchema> itemSchemas, string typeNameHint) {
        var firstItemSchema = itemSchemas.First();

        var itemSchema = new JsonSchema {
            Type = firstItemSchema.Type
        };

        if (firstItemSchema.Type == JsonObjectType.Object)
            foreach (var property in itemSchemas.SelectMany(s => s.Properties).GroupBy(p => p.Key))
                itemSchema.Properties[property.Key] = property.First().Value;

        AddSchemaDefinition(rootSchema, itemSchema, typeNameHint);

        schema.Item = new JsonSchema { Reference = itemSchema };
    }

    static void AddSchemaDefinition(JsonSchema rootSchema, JsonSchema schema, string typeNameHint) {
        if (string.IsNullOrEmpty(typeNameHint) || rootSchema.Definitions.ContainsKey(typeNameHint))
            rootSchema.Definitions[$"Anonymous{rootSchema.Definitions.Count + 1}"] = schema;
        else
            rootSchema.Definitions[typeNameHint] = schema;
    }

    [GeneratedRegex("^[0-2][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]$", RegexOptions.Compiled)]
    private static partial Regex DatePatternRegex();

    [GeneratedRegex("^[0-2][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] [0-9][0-9]:[0-9][0-9](:[0-9][0-9])?$", RegexOptions.Compiled)]
    private static partial Regex DateTimePatternRegex();

    [GeneratedRegex("^[0-9][0-9]:[0-9][0-9](:[0-9][0-9])?$", RegexOptions.Compiled)]
    private static partial Regex TimePatternRegex();
}