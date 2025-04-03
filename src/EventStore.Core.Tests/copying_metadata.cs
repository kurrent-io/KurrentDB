// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.ClientAPI;
using NUnit.Framework;

namespace EventStore.Core.Tests;

[TestFixture]
public class copying_metadata {
	[Test]
	public void copies_empty_metadata() {
		var empty = StreamMetadata.Build().Build();
		var copied = empty.Copy().Build();
		Assert.AreEqual(empty.AsJsonString(), copied.AsJsonString());
	}

	[Test]
	public void copies_all_values() {
		var source = StreamMetadata.Build()
			.SetCacheControl(TimeSpan.FromDays(1))
			.SetCustomProperty("Test", "Value")
			.SetReadRole("foo")
			.SetWriteRole("bar")
			.SetDeleteRole("baz")
			.SetMetadataReadRole("qux")
			.SetMetadataWriteRole("quux")
			.SetMaxAge(TimeSpan.FromHours(1))
			.SetMaxCount(2)
			.SetTruncateBefore(4)
			.Build();
		var copied = source.Copy().Build();
		Assert.AreEqual(source.AsJsonString(), copied.AsJsonString());
	}

	[Test]
	public void can_mutate_copy() {
		var source = StreamMetadata.Build()
			.SetCacheControl(TimeSpan.FromDays(1))
			.SetCustomProperty("Test", "Value")
			.SetReadRole("foo")
			.SetWriteRole("bar")
			.SetDeleteRole("baz")
			.SetMetadataReadRole("qux")
			.SetMetadataWriteRole("quux")
			.SetMaxAge(TimeSpan.FromHours(1))
			.SetMaxCount(2)
			.SetTruncateBefore(4)
			.Build();

		var expected = StreamMetadata.Build()
			.SetCacheControl(TimeSpan.FromDays(1))
			.SetCustomProperty("Test", "Value")
			.SetCustomProperty("Test2", "Value2")
			.SetReadRole("foo")
			.SetWriteRole("bar")
			.SetDeleteRole("baz")
			.SetMetadataReadRole("qux")
			.SetMetadataWriteRole("quux")
			.SetMaxAge(TimeSpan.FromHours(1))
			.SetMaxCount(4)
			.SetTruncateBefore(4)
			.Build();


		var copied = source.Copy()
			.SetMaxCount(4)
			.SetCustomProperty("Test2", "Value2")
			.Build();

		Assert.AreEqual(expected.AsJsonString(), copied.AsJsonString());
	}
}
