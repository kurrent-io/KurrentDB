// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexVAny;

[TestFixture]
public class create_index_map_from_non_existing_file {
	private IndexMap _map;

	[SetUp]
	public void Setup() {
		_map = IndexMapTestFactory.FromFile("thisfiledoesnotexist");
	}

	[Test]
	public void the_map_is_empty() {
		Assert.AreEqual(0, _map.InOrder().Count());
	}

	[Test]
	public void no_file_names_are_used() {
		Assert.AreEqual(0, _map.GetAllFilenames().Count());
	}

	[Test]
	public void prepare_checkpoint_is_equal_to_minus_one() {
		Assert.AreEqual(-1, _map.PrepareCheckpoint);
	}

	[Test]
	public void commit_checkpoint_is_equal_to_minus_one() {
		Assert.AreEqual(-1, _map.CommitCheckpoint);
	}
}
