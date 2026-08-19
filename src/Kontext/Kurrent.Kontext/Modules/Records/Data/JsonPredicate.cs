// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;

namespace Kurrent.Kontext.Data;

public readonly record struct JsonPredicate {
    readonly string _predicate;

    JsonPredicate(string predicate) => _predicate = predicate;

    public static JsonPredicate Create(string path) {
        EnsurePath(path);

        return new($"{path},null,null");
    }

    public static JsonPredicate Create<T>(string path, T value) {
        EnsurePath(path);

        var (type, text) = value switch {
            null           => ("null", "null"),
            string s       => ("str", s),
            bool b         => ("bool", b ? "true" : "false"),
            double d       => ("number", d.ToString("R", CultureInfo.InvariantCulture)),
            float f        => ("number", f.ToString("R", CultureInfo.InvariantCulture)),
            IFormattable n => ("number", n.ToString(null, CultureInfo.InvariantCulture)),
            _              => throw new NotSupportedException($"{typeof(T)} cannot be a JSON predicate value."),
        };

        // A ';' ends the predicate early, leaving a fragment the engine cannot parse, and it
        // panics instead of erroring. The grammar has no escape, so it is refused here.
        if (text.Contains(';'))
            throw new ArgumentException($"The value of '{path}' cannot contain ';'.", nameof(value));

        return new($"{path},{type},{text}");
    }

    public override string ToString() => _predicate;

    static void EnsurePath(string path) {
        if (path.Length == 0)
            throw new ArgumentException("A property path cannot be empty.", nameof(path));

        if (path.AsSpan().ContainsAny(',', ';'))
            throw new ArgumentException($"The property path '{path}' cannot contain ',' or ';'.", nameof(path));
    }

    internal static string Render(IReadOnlyCollection<JsonPredicate> predicates) =>
        string.Join(';', predicates.Select(predicate => predicate._predicate));
}
