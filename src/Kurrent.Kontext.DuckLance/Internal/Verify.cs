using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Microsoft.SemanticKernel;

[ExcludeFromCodeCoverage]
static partial class Verify {
    [GeneratedRegex("^[^.]+\\.[^.]+$")]
    private static partial Regex FilenameRegex();

    /// <summary>Equivalent of <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NotNull([System.Diagnostics.CodeAnalysis.NotNull] object? obj, [CallerArgumentExpression(nameof(obj))] string? paramName = null) =>
        ArgumentNullException.ThrowIfNull(obj, paramName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NotNullOrWhiteSpace([System.Diagnostics.CodeAnalysis.NotNull] string? str, [CallerArgumentExpression(nameof(str))] string? paramName = null) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(str, paramName);

    public static void NotNullOrEmpty<T>(IList<T> list, [CallerArgumentExpression(nameof(list))] string? paramName = null) {
        NotNull(list, paramName);

        if (list.Count == 0)
            throw new ArgumentException("The value cannot be empty.", paramName);
    }

    public static void True(bool condition, string message, [CallerArgumentExpression(nameof(condition))] string? paramName = null) {
        if (!condition)
            throw new ArgumentException(message, paramName);
    }

    public static void ValidFilename([System.Diagnostics.CodeAnalysis.NotNull] string? filename, [CallerArgumentExpression(nameof(filename))] string? paramName = null) {
        NotNullOrWhiteSpace(filename);

        if (!FilenameRegex().IsMatch(filename))
            throw new ArgumentException($"Invalid filename format: '{filename}'. Filename should consist of an actual name and a file extension.", paramName);
    }

    public static void ValidateUrl(string url, bool allowQuery = false, [CallerArgumentExpression(nameof(url))] string? paramName = null) {
        NotNullOrWhiteSpace(url, paramName);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException($"The `{url}` is not valid.", paramName);

        if (!allowQuery && !string.IsNullOrEmpty(uri.Query))
            throw new ArgumentException($"The `{url}` is not valid: it cannot contain query parameters.", paramName);

        if (!string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException($"The `{url}` is not valid: it cannot contain URL fragments.", paramName);
    }

    public static void StartsWith(
        [System.Diagnostics.CodeAnalysis.NotNull] string? text, string prefix, string message, [CallerArgumentExpression(nameof(text))] string? textParamName = null
    ) {
        Debug.Assert(prefix is not null);

        NotNullOrWhiteSpace(text, textParamName);

        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(textParamName, message);
    }

    public static void DirectoryExists(string path) {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory '{path}' could not be found.");
    }

    [DoesNotReturn]
    public static void ThrowArgumentInvalidName(string kind, string name, string? paramName) =>
        throw new ArgumentException($"A {kind} can contain only ASCII letters, digits, and underscores: '{name}' is not a valid name.", paramName);

    [DoesNotReturn]
    public static void ThrowArgumentNullException(string? paramName) => throw new ArgumentNullException(paramName);

    [DoesNotReturn]
    public static void ThrowArgumentWhiteSpaceException(string? paramName) =>
        throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName);

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRangeException<T>(string? paramName, T actualValue, string message) =>
        throw new ArgumentOutOfRangeException(paramName, actualValue, message);

    static readonly HashSet<string> InvalidLocationCharacters = [
        "://",
        "..",
        "\\",
        "/",
        "@",
        "?",
        "#",
        "[",
        "]",
        "&",
        ":",
        "<",
        ">",
        "'",
        "\"",
        "+",
        "|",
        "="
    ];

    /// <summary>Validates that a hostname segment (e.g. 'us-east1') is safe for use as a URL segment, preventing URL injection.</summary>
    public static void ValidHostnameSegment(string hostNameSegment, [CallerArgumentExpression(nameof(hostNameSegment))] string? paramName = null) {
        // Check for URL injection patterns and invalid characters
        if (InvalidLocationCharacters.Any(hostNameSegment.Contains))
            throw new ArgumentException($"The location '{hostNameSegment}' contains invalid characters that could enable URL injection.", paramName);

        // Validate location format (allows alphanumeric, hyphens, and underscores)
        // Common format examples: us-east1, europe-west4, asia-northeast1
        if (!Regex.IsMatch(hostNameSegment, @"^[a-zA-Z0-9][a-zA-Z0-9\-_]*[a-zA-Z0-9]$"))
            throw new ArgumentException(
                $"The location '{hostNameSegment}' is not valid. Location must start and end with alphanumeric characters and can contain hyphens and underscores.", paramName);
    }

    public static void NotLessThan(int value, int limit, [CallerArgumentExpression(nameof(value))] string? paramName = null) {
        if (value < limit)
            throw new ArgumentOutOfRangeException(paramName, $"{paramName} must be greater than or equal to {limit}.");
    }
}