// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexV4;

[TestFixture(PTable.IndexEntryV4Size), Explicit]
public class
	opening_a_ptable_with_more_than_32bits_of_records : IndexV1.opening_a_ptable_with_more_than_32bits_of_records {
	public opening_a_ptable_with_more_than_32bits_of_records(int indexEntrySize) : base(indexEntrySize) {
	}
}
