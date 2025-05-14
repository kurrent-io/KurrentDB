namespace Kurrent.Surge.Schema.Validation;

public record SchemaValidationResult {
    public List<SchemaValidationError> Errors { get; init; } = [];

    public bool IsValid => Errors.Count == 0;

    public static SchemaValidationResult Success() => new();

    public static SchemaValidationResult Failure(List<SchemaValidationError> errors) => new() { Errors = errors };

    public static SchemaValidationResult Failure(params SchemaValidationError[] errors) => Failure(errors.ToList());
}

public record SchemaValidationError {
    /// <summary>Gets the error message. </summary>
    public string ErrorMessage { get; init; }

    /// <summary>Gets the property name. </summary>
    public string? Property { get; init; }

    /// <summary>Gets the property path. </summary>
    public string? Path { get; init; }

    /// <summary>Gets the line number the validation failed on. </summary>
    public SchemaValidationErrorLineInfo? LineInfo { get; init; }

    /// <summary>Indicates whether the error contains line information.</summary>
    public bool HasLineInfo => LineInfo is not null;

    /// <summary>Returns a string that represents the current object.</summary>
    /// <returns>A string that represents the current object.</returns>
    /// <filterpriority>2</filterpriority>
    public override string ToString() =>
        Path is null ? $"{ErrorMessage}" : $"{ErrorMessage} @ {Path}";
}

public record SchemaValidationErrorLineInfo {
    public SchemaValidationErrorLineInfo() { }

    public SchemaValidationErrorLineInfo(uint lineNumber, uint linePosition) {
        LineNumber   = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>Gets the line number the validation failed on. </summary>
    public uint LineNumber { get; init; }

    /// <summary>Gets the line position the validation failed on. </summary>
    public uint LinePosition { get; init; }

    public override string ToString() => $"{LineNumber}:{LinePosition}";
}