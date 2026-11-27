// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Index;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Index.IndexV2;

[TestFixture(TypeArgs = [typeof(IndexEntry.V1)]), Explicit]
public class opening_a_ptable_with_more_than_32bits_of_records<T> : IndexV1.opening_a_ptable_with_more_than_32bits_of_records<T>
	where T : struct, IndexEntry.ILayout<T> {
}
