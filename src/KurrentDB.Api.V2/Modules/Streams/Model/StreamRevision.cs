// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

namespace KurrentDB.Api.Streams;

[PublicAPI]
[DebuggerDisplay("{ToDebugString()}")]
public record StreamRevision : IComparable<StreamRevision>, IComparable {
    /// <summary>
    /// The end of a stream. Use this when reading a stream backwards or subscribing live to a stream.
    /// </summary>
    public static readonly StreamRevision Max = new(long.MaxValue);

    /// <summary>
    /// The beginning (i.e., the first event) of a stream.
    /// </summary>
    public static readonly StreamRevision Min = new((long)0);

    /// <summary>
    /// A special value that refers to an invalid, unassigned or default revision.
    /// </summary>
    public static readonly StreamRevision Unset = new((long)-1000);

    StreamRevision(long value) => Value = value;

    public long Value { get; private init; }

    public static StreamRevision From(long value) =>
        value switch {
            -1000 => Unset,
            >= 0  => new StreamRevision(value),
            _     => throw new ArgumentException($"Invalid stream revision: {value}")
        };

    public static implicit operator long(StreamRevision _)                => _.Value;
    public static implicit operator StreamRevision(long _)                => From(_);
    //public static implicit operator ExpectedStreamState(StreamRevision _) => new(_.Value);

    public override string ToString() => Value.ToString();

    /// <summary>
    /// Creates a debug view.
    /// </summary>
    string ToDebugString() {
        var valueText = Value switch {
            -1000         => "Unset",
            0             => "Min",
            long.MaxValue => "Max",
            _             => Value.ToString()
        };

        return $"StreamRevision: {valueText}";
    }

    #region . relational members .

    public int CompareTo(StreamRevision? other) {
        if (ReferenceEquals(this, other)) return 0;

        return other is null ? 1 : Value.CompareTo(other.Value);
    }

    public int CompareTo(object? obj) {
        if (obj is null) return 1;
        if (ReferenceEquals(this, obj)) return 0;

        return obj is StreamRevision other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(StreamRevision)}");
    }

    public static bool operator <(StreamRevision? left, StreamRevision? right)  => Comparer<StreamRevision>.Default.Compare(left, right) < 0;
    public static bool operator >(StreamRevision? left, StreamRevision? right)  => Comparer<StreamRevision>.Default.Compare(left, right) > 0;
    public static bool operator <=(StreamRevision? left, StreamRevision? right) => Comparer<StreamRevision>.Default.Compare(left, right) <= 0;
    public static bool operator >=(StreamRevision? left, StreamRevision? right) => Comparer<StreamRevision>.Default.Compare(left, right) >= 0;

    #endregion
}
