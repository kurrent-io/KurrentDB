using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace EventStore.Streaming.Routing;

public interface IMessageContext {
    dynamic                  Message           { get; }
    CancellationToken        CancellationToken { get; }
    MessageContextProperties Properties        { get; }
}

[PublicAPI]
public class MessageContext(dynamic message, CancellationToken cancellationToken) : IMessageContext {
    public dynamic                  Message           { get; } = message;
    public CancellationToken        CancellationToken { get; } = cancellationToken;
    public MessageContextProperties Properties        { get; } = new();
}

/// <summary>
/// Represents a collection of custom context properties.
/// </summary>
[DebuggerDisplay("{Options}")]
public sealed class MessageContextProperties {
    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    internal IDictionary<string, object?> Options { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// Gets the value of a given property.
    /// </summary>
    /// <param name="key">Strongly typed key to get the value of the property.</param>
    /// <param name="value">Returns the value of the property.</param>
    /// <typeparam name="TValue">The type of property value as defined by <paramref name="key"/> parameter.</typeparam>
    /// <returns>True, if a property was retrieved.</returns>
    public bool TryGetValue<TValue>(MessageContextPropertyKey<TValue> key, [MaybeNullWhen(false)] out TValue value) {
        if (Options.TryGetValue(key.Key, out object? val) && val is TValue typedValue) {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets the value of a given property with a fallback default value.
    /// </summary>
    /// <param name="key">Strongly typed key to get the value of the property.</param>
    /// <param name="defaultValue">The default value to use if property is not found.</param>
    /// <typeparam name="TValue">The type of property value as defined by <paramref name="key"/> parameter.</typeparam>
    /// <returns>The property value or the default value.</returns>
    public TValue GetValue<TValue>(MessageContextPropertyKey<TValue> key, TValue defaultValue) =>
        TryGetValue(key, out var value) ? value : defaultValue;

    /// <summary>
    /// Sets the value of a given property.
    /// </summary>
    /// <param name="key">Strongly typed key to get the value of the property.</param>
    /// <param name="value">Returns the value of the property.</param>
    /// <typeparam name="TValue">The type of property value as defined by <paramref name="key"/> parameter.</typeparam>
    public MessageContextProperties Set<TValue>(MessageContextPropertyKey<TValue> key, TValue value) {
        Options[key.Key] = value;
        return this;
    }

    /// <summary>
    /// Clears all properties from the context.
    /// </summary>
    public void Clear() => Options.Clear();

    internal void AddOrReplaceProperties(MessageContextProperties other) {
        // try to avoid enumerator allocation
        if (other.Options is Dictionary<string, object?> otherOptions) {
            foreach (var pair in otherOptions)
                Options[pair.Key] = pair.Value;
        }
        else {
            foreach (var pair in other.Options)
                Options[pair.Key] = pair.Value;
        }
    }
}

/// <summary>
/// Represents a key used by <see cref="MessageContextProperties"/>.
/// </summary>
/// <typeparam name="TValue">The type of the value of the property.</typeparam>
public readonly struct MessageContextPropertyKey<TValue> {
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageContextPropertyKey{TValue}"/> struct.
    /// </summary>
    /// <param name="key">The key name.</param>
    public MessageContextPropertyKey(string key) => Key = Ensure.NotNull(key);

    /// <summary>
    /// Gets the name of the key.
    /// </summary>
    public string Key { get; }

    /// <inheritdoc/>
    public override string ToString() => Key;
}