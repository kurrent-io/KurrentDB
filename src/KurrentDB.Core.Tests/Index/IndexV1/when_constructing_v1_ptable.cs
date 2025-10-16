// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Exceptions;
using KurrentDB.Core.Index;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Index.IndexV1;

public class when_constructing_v1_ptable : SpecificationWithDirectoryPerTestFixture {
	[Test]
	public void an_exception_is_thrown() {
		Assert.Throws<CorruptIndexException>(() => {
			using var table = PTable.FromMemtable(
				new HashListMemTable(PTableVersions.IndexV1, maxSize: 20),
				GetTempFilePath());
		});
	}
}
