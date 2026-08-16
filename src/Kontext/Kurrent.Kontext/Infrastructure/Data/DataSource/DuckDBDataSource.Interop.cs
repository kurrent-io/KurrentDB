// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using System.Runtime.CompilerServices;
using DuckDB.NET.Data;

namespace Kurrent.Quack;

static class DuckDBAdvancedConnectionAccessors {
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryReset")]
    static extern bool UnsafeTryResetCall(DuckDBAdvancedConnection target);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "HasOpenTransaction")]
    static extern bool UnsafeHasOpenTransactionCall(DuckDBAdvancedConnection target);

    extension(DuckDBAdvancedConnection connection) {
        public bool UnsafeTryReset()           => UnsafeTryResetCall(connection);
        public bool UnsafeHasOpenTransaction() => UnsafeHasOpenTransactionCall(connection);
    }
}

/// <summary>
/// Shares one in-memory DuckDB database between several connections, scoped to a single data source.
/// </summary>
/// <remarks>
/// <para>
/// A <c>DataSource=:memory:</c> connection gets its own private database, which would make pooling
/// meaningless. The driver can share one — <c>:memory:?cache=shared</c> — but resolves it through a
/// <c>static</c> dictionary keyed by the data source string, so that is process-wide and two data
/// sources would collide. We need one database per data source instance, which no connection string
/// expresses.
/// </para>
/// <para>
/// So we do what <c>DuckDBConnection.Duplicate()</c> does. It exists for exactly this but returns
/// <see cref="DuckDBConnection"/>, so it cannot produce the <see cref="DuckDBAdvancedConnection"/>
/// subclass we hand out. Setting the same three private members makes the driver's <c>Open()</c>
/// take its duplication branch, joining the prototype's database instead of resolving a new one.
/// </para>
/// <para>
/// Binary-level dependency on DuckDB.NET internals: keep consistent with <c>Duplicate()</c> and
/// re-check on upgrade. Verified against 1.5.3. The prototype must stay open for the data source's
/// lifetime — the shared database is reference counted and dies at zero.
/// </para>
/// </remarks>
static class InMemoryConnectionState {
	internal static void Copy(DuckDBConnection prototype, DuckDBConnection destination) {
		// Selects the DuplicateConnectionReference branch in DuckDBConnection.Open().
		GetInMemoryDuplication(destination) = true;

		// The already-open database and its reference count.
		var connectionReference = GetConnectionReferenceField()
			?? throw new InvalidOperationException(MissingField("connectionReference"));
		connectionReference.SetValue(destination, connectionReference.GetValue(prototype));

		// The parsed connection string, so the duplicate does not resolve a database of its own.
		var parsedConnection = GetParsedConnectionField()
			?? throw new InvalidOperationException(MissingField("parsedConnection"));
		parsedConnection.SetValue(destination, GetParsedConnection(prototype));

		// Silently skipping either field would leave the connection to resolve a database of its own,
		// so a driver upgrade that renames them has to fail here rather than three calls later.
		static string MissingField(string name)
			=> $"DuckDB.NET's DuckDBConnection.{name} field was not found. In-memory database sharing "
			 + "depends on it; the driver's internals have changed and InMemoryConnectionState needs updating.";

		[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inMemoryDuplication")]
		static extern ref bool GetInMemoryDuplication(DuckDBConnection connection);

		[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ParsedConnection")]
		[return: UnsafeAccessorType("DuckDB.NET.Data.Connection.DuckDBConnectionString, DuckDB.NET.Data")]
		static extern object GetParsedConnection(DuckDBConnection connection);

		// Private instance fields with no UnsafeAccessor-compatible signature, so reflection. Both are
		// set once per connection, before it is opened.
		static FieldInfo? GetParsedConnectionField()
			=> typeof(DuckDBConnection).GetField("parsedConnection",
				BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance);

		static FieldInfo? GetConnectionReferenceField()
			=> typeof(DuckDBConnection).GetField("connectionReference",
				BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance);
	}
}
