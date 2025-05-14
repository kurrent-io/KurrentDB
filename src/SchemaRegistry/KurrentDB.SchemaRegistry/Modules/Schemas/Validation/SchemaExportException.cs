namespace Kurrent.Surge.Schema.Validation;

public class SchemaExportException(Exception innerException)
    : Exception("Failed to export schema", innerException) { }