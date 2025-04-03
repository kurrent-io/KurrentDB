// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.IO;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag;

[TestFixture]
public class checkpoint_tag_to_from_json {
	[Test]
	public void stream_position_should_not_overflow() {
		var checkpointTag = CheckpointTag.FromStreamPosition(1, "test", 9876543210L);
		var json = checkpointTag.ToJsonString();

		var jsonReader = new JsonTextReader(new StringReader(json));
		var checkpointTagFromJson = CheckpointTag.FromJson(jsonReader, new ProjectionVersion(0, 0, 0));
		Assert.AreEqual(9876543210L, checkpointTagFromJson.Tag.Streams["test"]);
	}

	[Test]
	public void data_and_catalog_position_should_not_overflow() {
		var checkpointTag =
			CheckpointTag.FromByStreamPosition(1, "catalog", 9876543210L, "data", 9876543211L, 9876543212L);
		var json = checkpointTag.ToJsonString();

		var jsonReader = new JsonTextReader(new StringReader(json));
		var checkpointTagFromJson = CheckpointTag.FromJson(jsonReader, new ProjectionVersion(0, 0, 0));
		Assert.AreEqual(9876543210L, checkpointTagFromJson.Tag.CatalogPosition);
		Assert.AreEqual(9876543211L, checkpointTagFromJson.Tag.DataPosition);
	}

	[Test]
	public void prepare_and_commit_positions_should_not_overflow() {
		var checkpointTag =
			CheckpointTag.FromPosition(1, 9876543210L, 9876543211L);
		var json = checkpointTag.ToJsonString();

		var jsonReader = new JsonTextReader(new StringReader(json));
		var checkpointTagFromJson = CheckpointTag.FromJson(jsonReader, new ProjectionVersion(0, 0, 0));
		Assert.AreEqual(9876543210L, checkpointTagFromJson.Tag.CommitPosition);
		Assert.AreEqual(9876543211L, checkpointTagFromJson.Tag.PreparePosition);
	}
}
