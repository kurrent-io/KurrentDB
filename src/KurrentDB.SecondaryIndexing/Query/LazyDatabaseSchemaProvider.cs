// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.Query;

// We need lazy provider to avoid circular dependency between IndexingDbSchema and QueryEngine
internal sealed class LazyDatabaseSchemaProvider(IServiceProvider provider) : IDatabaseSchemaProvider {
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private IDatabaseSchemaProvider Provider
		=> field ??= provider.GetRequiredService<QueryEngine>();

	IEnumerable<string> IDatabaseSchemaProvider.Tables => Provider.Tables;
}
