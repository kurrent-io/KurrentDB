namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// The wire-ready vectors for ONE record, addressed by storage column name — never by position.
/// Codec implementers only ever CONSUME slots (in <c>Encode</c>); construction stays internal to the
/// assembly so a mis-built instance can't silently break the name↔column association.
/// </summary>
public readonly struct VectorSlots(string[] columns, float[]?[] vectors) {
    // Parallel arrays: _vectors[i] is the vector for column _columns[i]. Both stay null on the
    // default value, which is exactly how a vector-less record travels (Count 0, Single null).
    readonly string[]?   _columns = columns;
    readonly float[]?[]? _vectors = vectors;

    /// <summary>The number of vector slots this record carries.</summary>
    public int Count => _columns?.Length ?? 0;

    /// <summary>
    /// The vector for the named column. Unknown names throw loudly — returning null instead would
    /// let a typo silently write a NULL vector.
    /// </summary>
    public float[]? this[string column] {
        get {
            // Linear scan on purpose: records carry 1-3 slots, so a dictionary would cost more than it saves.
            for (var i = 0; i < Count; i++) {
                if (_columns![i] == column)
                    return _vectors![i];
            }

            throw new KeyNotFoundException(
                $"No vector slot for column '{column}'. Available slots: "
              + (Count == 0 ? "none" : string.Join(", ", _columns!)) + ".");
        }
    }

    /// <summary>
    /// The only slot's vector, or null when there are no slots. Records with several vector columns
    /// must address slots by column name — asking for a single one then throws.
    /// </summary>
    public float[]? Single =>
        Count switch {
            0 => null,
            1 => _vectors![0],
            _ => throw new InvalidOperationException($"The record has {Count} vector slots — address them by column name, not through {nameof(Single)}.")
        };
}
