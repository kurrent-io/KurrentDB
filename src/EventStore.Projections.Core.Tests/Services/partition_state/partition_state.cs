// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state;

public static class partition_state {
	[TestFixture]
	public class when_creating {
		[Test]
		public void throws_argument_null_exception_if_state_is_null() {
			Assert.Throws<ArgumentNullException>(() => {
				new PartitionState(null, "result", CheckpointTag.FromPosition(0, 100, 50));
			});
		}

		[Test]
		public void throws_argument_null_exception_if_caused_by_is_null() {
			Assert.Throws<ArgumentNullException>(() => { new PartitionState("state", "result", null); });
		}

		[Test]
		public void can_be_created() {
			new PartitionState("state", "result", CheckpointTag.FromPosition(0, 100, 50));
		}
	}

	[TestFixture]
	public class can_be_deserialized_from_serialized_form {
		[Test]
		public void simple_object() {
			AssertCorrect(@"");
			AssertCorrect(@"{""a"":""b""}");
			AssertCorrect(@"{""a"":""b"",""c"":1}");
			AssertCorrect(@"{""z"":null,""a"":""b"",""c"":1}");
		}

		[Test]
		public void complex_object() {
			AssertCorrect(@"{""a"":""b"",""c"":[1,2,3]}");
			AssertCorrect(@"{""a"":""b"",""c"":{""a"":""b""}}");
			AssertCorrect(@"{""a"":""b"",""c"":[{},[],null]}");
		}

		[Test]
		public void array() {
			AssertCorrect(@"[]");
			AssertCorrect(@"[""one"",""two""]");
			AssertCorrect(@"[{""data"":{}}]");
		}

		[Test]
		public void dates() {
			foreach(var info in TimeZoneInfo.GetSystemTimeZones())
				AssertCorrect($@"[""{DateTimeOffset.UtcNow.ToOffset(info.BaseUtcOffset):yyyy-MM-ddThh:mm:sszzz}""]");
		}

		[Test]
		public void null_deserialization() {
			var deserialized = PartitionState.Deserialize(null, CheckpointTag.FromPosition(0, 100, 50));
			Assert.AreEqual("", deserialized.State);
			Assert.IsNull(deserialized.Result);
		}

		private void AssertCorrect(string state, string result = null) {
			var partitionState = new PartitionState(state, result, CheckpointTag.FromPosition(0, 100, 50));
			var serialized = partitionState.Serialize();
			var deserialized = PartitionState.Deserialize(serialized, CheckpointTag.FromPosition(0, 100, 50));

			Assert.AreEqual(partitionState.State, deserialized.State);
			Assert.AreEqual(partitionState.Result, deserialized.Result);
		}
	}
}
