// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;

namespace KurrentDB.SecondaryIndexing.Indexes.Default;

static class DefaultIndex {
	public static (TFPos PreviousPosition, TFPos NextPosition) GetPrevNextPosition(DuckDBConnectionPool connectionPool, long firstRowId, long lastRowId) {
		var before = firstRowId - 1;
		var after = lastRowId + 1;
		var result = connectionPool.Query<GetPrevNextPositionQueryArgs, PositionQueryRecord, GetPrevNextPositionQuery>(new(before, after));

		if (result.Count == 0) {
			return (TFPos.FirstRecordOfTf, TFPos.FirstRecordOfTf);
		}

		var previous = TFPos.FirstRecordOfTf;
		var next = TFPos.HeadOfTf;
		for (var i = 0; i < result.Count; i++) {
			var record = result[i];
			if (record.RowId == before) {
				previous = new(record.CommitPosition ?? record.LogPosition, record.LogPosition);
				continue;
			}
			if (record.RowId == after) {
				next = new(record.CommitPosition ?? record.LogPosition, record.LogPosition);
			}
		}
		return (previous, next);
	}
}
