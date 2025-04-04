using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.checkpoint_tag {
	[TestFixture]
	public class checkpoint_tag_by_stream_positions_when_updating {
		private readonly CheckpointTag _a1b1 = CheckpointTag.FromStreamPositions(
			1, new Dictionary<string, long> { { "a", 1 }, { "b", 1 } });

		[Test]
		public void updated_position_is_correct() {
			var updated = _a1b1.UpdateStreamPosition("a", 2);
			Assert.AreEqual(2, updated.Streams["a"]);
		}

		[Test]
		public void other_stream_position_is_correct() {
			var updated = _a1b1.UpdateStreamPosition("a", 2);
			Assert.AreEqual(1, updated.Streams["b"]);
		}

		[Test]
		public void streams_are_correct() {
			var updated = _a1b1.UpdateStreamPosition("a", 2);
			Assert.AreEqual(2, updated.Streams.Count);
			Assert.IsTrue(updated.Streams.Any(v => v.Key == "a"));
			Assert.IsTrue(updated.Streams.Any(v => v.Key == "b"));
		}
	}
}
