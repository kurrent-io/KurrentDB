// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Api.Streams;

/// <summary>
/// Represents the name of a stream in KurrentDB.
/// </summary>
[PublicAPI]
public readonly record struct StreamName {
    StreamName(string value) => Value = value;

    /// <summary>
    /// Represents an uninitialized or empty stream name.
    /// </summary>
    public static readonly StreamName None = new("");

    /// <summary>
    /// The underlying string value of the stream name.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns the string representation of the stream name.
    /// </summary>
    /// <returns>The string value of the stream name.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if the value is invalid (i.e., null, empty, or whitespace).
    /// </summary>
    public void ThrowIfInvalid() => ArgumentException.ThrowIfNullOrWhiteSpace(Value);

    /// <summary>
    /// Creates a <see cref="StreamName"/> from a string value.
    /// </summary>
    /// <param name="value">The string value to create the stream name from. Cannot be null, empty, or whitespace.</param>
    /// <returns>A new <see cref="StreamName"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="value"/> is null, empty, or consists only of white-space characters.</exception>
    public static StreamName From(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(value);
    }

    public StreamName Metastream => new($"$${Value}");

    public bool IsMetastream => Value.StartsWith("$$");

    /// <summary>
    /// Whether the specified stream is a system stream.
    /// </summary>
    public bool IsSystemStream => Value.StartsWith("$$");

    /// <summary>
    /// A stream containing all events in the KurrentDB transaction file.
    /// </summary>
    public const string AllStream = "$all";

    /// <summary>
    /// A stream containing links pointing to each stream in the KurrentDB.
    /// </summary>
    public const string StreamsStream = "$streams";

    /// <summary>
    /// A stream containing system settings.
    /// </summary>
    public const string SettingsStream = "$settings";

    /// <summary>
    /// A stream containing statistics.
    /// </summary>
    public const string StatsStreamPrefix = "$stats";

    /// <summary>
    ///
    /// </summary>
    public bool IsAllStream => Value == AllStream;

    /// <summary>
    ///
    /// </summary>
    public bool IsStreamsStream => Value == StreamsStream;

    /// <summary>
    /// Gets a value indicating whether the current stream represents the system settings stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The system settings stream is identified by the constant value <c>"$settings"</c>. This property returns
    /// <see langword="true"/> if the <see cref="Value"/> of the current <see cref="StreamName"/> matches
    /// this predefined value, indicating that the stream contains system-level settings.
    /// </para>
    /// <para>
    /// The settings stream is typically utilized to store and retrieve configuration values and other
    /// system-wide metadata that influence the behavior of the database.
    /// </para>
    /// <b>Usage notes:</b>
    /// <list type="bullet">
    /// <item>
    /// <description>This property returns <see langword="false"/> for streams that do not match the
    /// system settings stream identifier.</description>
    /// </item>
    /// <item>
    /// <description>Ensure your code accounts for this value when determining specific handling
    /// for system-related streams.</description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <value>
    /// <see langword="true"/> if the current stream is the system settings stream; otherwise, <see langword="false"/>.
    /// </value>
    /// <example>
    /// The following example demonstrates how to check if a stream is the settings stream:
    /// <code>
    /// var streamName = new StreamName("$settings");
    /// if (streamName.IsSettingsStream)
    /// {
    /// Console.WriteLine("This is the settings stream.");
    /// }
    /// else
    /// {
    /// Console.WriteLine("This is not the settings stream.");
    /// }
    /// </code>
    /// </example>
    public bool IsSettingsStream => Value == SettingsStream;

    /// <summary>
    ///
    /// </summary>
    public bool IsStatsStream => Value.StartsWith(StatsStreamPrefix);

    /// <summary>
    /// Implicitly converts a string to a <see cref="StreamName"/>.
    /// </summary>
    /// <param name="stream">The string to convert. Cannot be null, empty, or whitespace.</param>
    /// <returns>A new <see cref="StreamName"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="stream"/> is null, empty, or consists only of white-space characters.</exception>
    public static implicit operator StreamName(string stream)  => From(stream);

    /// <summary>
    /// Implicitly converts a <see cref="StreamName"/> to its string representation.
    /// </summary>
    /// <param name="stream">The <see cref="StreamName"/> to convert.</param>
    /// <returns>The string value of the stream name.</returns>
    public static implicit operator string(StreamName stream) => stream.ToString();
}
