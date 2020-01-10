﻿using System;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Bus;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.ClientAPI {
	namespace with_standard_projections_running {
		public abstract class when_deleting_stream_base : specification_with_standard_projections_running {
			[Test, Category("Network")]
			public async Task streams_stream_exists() {
				Assert.AreEqual(
					SliceReadStatus.Success,
					(await _conn.ReadStreamEventsForwardAsync("$streams", 0, 10, false, _admin)).Status);
			}

			[Test, Category("Network")]
			public void deleted_stream_events_are_indexed() {
				StreamEventsSlice slice = null;
				AssertEx.IsOrBecomesTrue(() => {
					slice = _conn.ReadStreamEventsForwardAsync("$ce-cat", 0, 10, true, _admin).Result;
					Assert.AreEqual(SliceReadStatus.Success, slice.Status);
					return 3 == slice.Events.Length;
				});
				var deletedLinkMetadata = slice.Events[2].Link.Metadata;
				Assert.IsNotNull(deletedLinkMetadata);

				var checkpointTag = Encoding.UTF8.GetString(deletedLinkMetadata).ParseCheckpointExtraJson();
				Assert.IsTrue(checkpointTag.TryGetValue("$deleted", out _));
				Assert.IsTrue(checkpointTag.TryGetValue("$o", out var originalStream));
				Assert.AreEqual("cat-1", ((JValue)originalStream).Value);
			}

			[Test, Category("Network")]
			public void deleted_stream_events_are_indexed_as_deleted() {
				AssertEx.IsOrBecomesTrue(() => {
					var slice = _conn.ReadStreamEventsForwardAsync("$et-$deleted", 0, 10, true, _admin).Result;
					return SliceReadStatus.Success == slice.Status && slice.Events.Length == 1;
				});				
			}

			protected override async Task When() {
				await base.When();
				var r1 = await _conn.AppendToStreamAsync(
						"cat-1", ExpectedVersion.NoStream, _admin,
						new EventData(Guid.NewGuid(), "type1", true, Encoding.UTF8.GetBytes("{}"), null))
					;

				var r2 = await _conn.AppendToStreamAsync(
					"cat-1", r1.NextExpectedVersion, _admin,
					new EventData(Guid.NewGuid(), "type1", true, Encoding.UTF8.GetBytes("{}"), null));

				await _conn.DeleteStreamAsync("cat-1", r2.NextExpectedVersion, GivenDeleteHardDeleteStreamMode(),
						_admin)
					;
				WaitIdle();
				if (!GivenStandardProjectionsRunning()) {
					await EnableStandardProjections();
					WaitIdle();
				}
			}

			protected abstract bool GivenDeleteHardDeleteStreamMode();
		}

		[TestFixture]
		public class when_hard_deleting_stream : when_deleting_stream_base {
			protected override bool GivenDeleteHardDeleteStreamMode() {
				return true;
			}
		}

		[TestFixture]
		public class when_soft_deleting_stream : when_deleting_stream_base {
			protected override bool GivenDeleteHardDeleteStreamMode() {
				return false;
			}
		}

		[TestFixture]
		public class when_hard_deleting_stream_and_starting_standard_projections : when_deleting_stream_base {
			protected override bool GivenDeleteHardDeleteStreamMode() {
				return true;
			}

			protected override bool GivenStandardProjectionsRunning() {
				return false;
			}
		}

		[TestFixture]
		public class when_soft_deleting_stream_and_starting_standard_projections : when_deleting_stream_base {
			protected override bool GivenDeleteHardDeleteStreamMode() {
				return false;
			}

			protected override bool GivenStandardProjectionsRunning() {
				return false;
			}
		}
	}
}
