// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit;

namespace Kurrent.Quack.ConnectionPool;

public sealed class DuckDBConnectionPoolTests : DuckDbTests<DuckDBConnectionPoolTests> {
	[Fact]
	public void RentReturn() {
		using var pool = new DuckDBConnectionPool(ConnectionString);

		DuckDBAdvancedConnection connection1;
		using (pool.Rent(out connection1)) {
			Assert.NotNull(connection1.ServerVersion);
		}

		DuckDBAdvancedConnection connection2;
		using (pool.Rent(out connection2)) {
			Assert.NotNull(connection2.ServerVersion);
		}

		Assert.Same(connection1, connection2);
	}
}
