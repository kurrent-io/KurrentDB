// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using DotNext.Runtime.InteropServices;
using DuckDB.NET.Native;
using Kurrent.Quack;
using Kurrent.Quack.Functions;
using KurrentDB.SecondaryIndexing.Query;

namespace KurrentDB.SecondaryIndexing.Storage;

internal sealed class QueryTablesFunction(IDatabaseSchemaProvider provider) : TableFunction(Name) {
	public new const string Name = "kdb_table_names";

	protected override IReadOnlyList<ParameterDefinition> Parameters => [];

	protected override void Bind(BindingContext context) {
		context.AddColumn("name"u8, DuckDBType.Varchar);
		var tables = new List<string>(provider.Tables.Select(static info => info.FullName));
		tables.Sort(StringComparer.Ordinal);
		context.SetExpectedRowsCountHint(tables.Count, exact: true);
		context.SetUserData(tables);
	}

	protected override void Initialize(InitializationContext context) {
		if (context.IsThreadLocal) {
			var tables = BindingContext.GetUserData<List<string>>(context).Value
			             ?? throw new UnreachableException("A list of tables must be set.");
			context.SetUserData<IEnumerator<string>>(tables.GetEnumerator());
		} else {
			context.SetMaxThreadsHint(1);
		}
	}

	protected override void ProduceRows(ExecutionContext context, ref DataChunk.Builder builder) {
		var enumerator = InitializationContext
			                 .GetUserData<IEnumerator<string>>(context, isThreadLocal: true).Value
		                 ?? throw new UnreachableException("Iterator of tables must be set.");
		var maxRowCount = DataChunk.MaxRowCount;
		int actualRowCount;

		var column = builder[0];
		for (actualRowCount = 0; actualRowCount < maxRowCount && enumerator.MoveNext(); actualRowCount++) {
			column.SetValue(actualRowCount, enumerator.Current);
		}

		if (actualRowCount is 0)
			enumerator.Dispose();

		builder.RowCount = actualRowCount;
	}
}
