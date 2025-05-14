namespace Kurrent.Surge.Schema.Validation;

public class SchemaValidationException(Exception innerException)
    : Exception("Failed to validate schema", innerException) { }