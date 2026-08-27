// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DuckDB.NET.Data;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Testing;

/// <summary>
/// Folds surface forms for a comparison that happens outside a query, through the same macro every
/// Kontext connection defines. Its own in-memory engine rather than the store's: the fold reads no
/// data, so it needs no attachment, and a private connection keeps per-span folding off the Lance
/// pool. Folds are pure, so each one is answered from the memo after the first. Not thread safe.
/// </summary>
public sealed class EntityFolder : IDisposable {
    readonly DuckDBConnection           _connection;
    readonly DuckDBCommand              _command;
    readonly Dictionary<string, string> _folded = [];

    public EntityFolder() {
        _connection = new("DataSource=:memory:");
        _connection.Open();

        using (var load = _connection.CreateCommand()) {
            load.CommandText = $"INSTALL fts; LOAD fts; {KontextDataSource.FoldMacro}";
            load.ExecuteNonQuery();
        }

        _command = _connection.CreateCommand();
        _command.CommandText = "SELECT fold($text)";
        _command.Parameters.Add(new DuckDBParameter("text", ""));
        _command.Prepare();
    }

    public string Fold(string text) {
        if (_folded.TryGetValue(text, out var folded))
            return folded;

        _command.Parameters[0].Value = text;

        folded        = (string)_command.ExecuteScalar()!;
        _folded[text] = folded;

        return folded;
    }

    public void Dispose() {
        _command.Dispose();
        _connection.Dispose();
    }
}
