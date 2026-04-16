// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack;

namespace KurrentDB.SecondaryIndexing.Tests.Migration;

partial class MigrationTests {
	[Fact]
	public void UpgradeToV1() {
		// Setup
		UpgradeTo(desiredVersion: 1);

		// Check new schema
		CheckIdxAllTable(_connection);
		CheckUserIndexTable(_connection);

		static void CheckIdxAllTable(DuckDBAdvancedConnection connection) {
			var columns = connection
				.ExecuteQuery<ValueTuple<string>, string, ColumnNamesQuery>(new("idx_all"))
				.ToHashSet();

			Assert.Contains("record_id", columns);
			Assert.Contains("created_at", columns);
			Assert.Contains("expires_at", columns);
			Assert.Contains("deleted", columns);
			Assert.Contains("schema_name", columns);
			Assert.Contains("stream_revision", columns);

			Assert.DoesNotContain("event_type", columns);
			Assert.DoesNotContain("created", columns);
			Assert.DoesNotContain("event_number", columns);
			Assert.DoesNotContain("is_deleted", columns);
			Assert.DoesNotContain("expires", columns);
		}

		static void CheckUserIndexTable(DuckDBAdvancedConnection connection) {
			var columns = connection
				.ExecuteQuery<ValueTuple<string>, string, ColumnNamesQuery>(new("idx_user__MyIndex"))
				.ToHashSet();

			Assert.Contains("record_id", columns);
			Assert.Contains("created_at", columns);
			Assert.Contains("stream_revision", columns);

			Assert.DoesNotContain("created", columns);
			Assert.DoesNotContain("event_number", columns);
		}
	}
}
