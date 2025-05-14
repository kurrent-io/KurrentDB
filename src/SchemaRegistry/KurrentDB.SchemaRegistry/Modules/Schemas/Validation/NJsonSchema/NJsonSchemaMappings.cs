using Humanizer;

namespace Kurrent.Surge.Schema.Validation;

static class NJsonSchemaMappings {
    public static SchemaValidationError Map(this NJsonSchema.Validation.ValidationError value) =>
        new() {
            ErrorMessage = value.Kind.Humanize(),
            Property     = value.Property,
            Path         = value.Path,
            LineInfo     = value.HasLineInfo
                ? new SchemaValidationErrorLineInfo {
                    LineNumber   = (uint)value.LineNumber,
                    LinePosition = (uint)value.LinePosition
                }
                : null
        };

    public static List<SchemaValidationError> Map(this IEnumerable<NJsonSchema.Validation.ValidationError> values) =>
        values.Select(Map).ToList();
}