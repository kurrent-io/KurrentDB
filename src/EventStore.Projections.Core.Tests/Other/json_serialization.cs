// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Other;

[TestFixture]
class can_serialize_and_deserialize {
	private ProjectionVersion _version;

	[SetUp]
	public void setup() {
		_version = new ProjectionVersion(1, 0, 0);
	}

	[Test]
	public void position_based_checkpoint_tag() {
		CheckpointTag tag = CheckpointTag.FromPosition(1, -1, 0);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void prepare_position_based_checkpoint_tag_zero() {
		CheckpointTag tag = CheckpointTag.FromPreparePosition(1, 0);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void prepare_position_based_checkpoint_tag_minus_one() {
		CheckpointTag tag = CheckpointTag.FromPreparePosition(1, -1);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void prepare_position_based_checkpoint_tag() {
		CheckpointTag tag = CheckpointTag.FromPreparePosition(1, 1234);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void stream_based_checkpoint_tag() {
		CheckpointTag tag = CheckpointTag.FromStreamPosition(1, "$ce-account", 12345);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
		Assert.IsNull(back.CommitPosition);
	}

	[Test]
	public void streams_based_checkpoint_tag() {
		CheckpointTag tag = CheckpointTag.FromStreamPositions(1, new Dictionary<string, long> {{"a", 1}, {"b", 2}});
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
		Assert.IsNull(back.CommitPosition);
	}

	[Test]
	public void event_by_type_index_based_checkpoint_tag() {
		CheckpointTag tag = CheckpointTag.FromEventTypeIndexPositions(
			0, new TFPos(100, 50), new Dictionary<string, long> {{"a", 1}, {"b", 2}});
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void phase_based_checkpoint_tag_completed() {
		CheckpointTag tag = CheckpointTag.FromPhase(2, completed: false);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void phase_based_checkpoint_tag_incomplete() {
		CheckpointTag tag = CheckpointTag.FromPhase(0, completed: true);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void by_stream_based_checkpoint_tag() {
		CheckpointTag tag = CheckpointTag.FromByStreamPosition(0, "catalog", 1, "data", 2, 12345);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void by_stream_based_checkpoint_tag_zero() {
		CheckpointTag tag = CheckpointTag.FromByStreamPosition(0, "catalog", -1, null, -1, 12345);
		byte[] bytes = tag.ToJsonBytes(_version);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTag back = instring.ParseCheckpointTagJson();
		Assert.AreEqual(tag, back);
	}

	[Test]
	public void extra_metadata_are_preserved() {
		CheckpointTag tag = CheckpointTag.FromPosition(0, -1, 0);
		var extra = new Dictionary<string, JToken> {{"$$a", new JRaw("\"b\"")}, {"$$c", new JRaw("\"d\"")}};
		byte[] bytes = tag.ToJsonBytes(_version, extra);
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		CheckpointTagVersion back = instring.ParseCheckpointTagVersionExtraJson(_version);
		Assert.IsNotNull(back.ExtraMetadata);
		JToken v;
		Assert.IsTrue(back.ExtraMetadata.TryGetValue("$$a", out v));
		Assert.AreEqual("b", (string)((JValue)v).Value);
		Assert.IsTrue(back.ExtraMetadata.TryGetValue("$$c", out v));
		Assert.AreEqual("d", (string)((JValue)v).Value);
	}

	[Test]
	public void can_deserialize_readonly_fields() {
		TestData data = new TestData("123");
		byte[] bytes = data.ToJsonBytes();
		string instring = Helper.UTF8NoBom.GetString(bytes);
		Console.WriteLine(instring);

		TestData back = instring.ParseJson<TestData>();
		Assert.AreEqual(data, back);
	}

	private class TestData {
		public readonly string Data;

		public TestData(string data) {
			Data = data;
		}

		protected bool Equals(TestData other) {
			return string.Equals(Data, other.Data);
		}

		public override bool Equals(object obj) {
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != GetType()) return false;
			return Equals((TestData)obj);
		}

		public override int GetHashCode() {
			return (Data != null ? Data.GetHashCode() : 0);
		}

		public override string ToString() {
			return string.Format("Data: {0}", Data);
		}
	}
}
