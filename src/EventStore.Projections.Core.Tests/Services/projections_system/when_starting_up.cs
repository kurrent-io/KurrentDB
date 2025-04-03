// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using NUnit.Framework;
using EventStore.Core.Messages;
using System;
using System.Net;
using EventStore.Core.Cluster;
using EventStore.Core.Data;
using EventStore.Core.Tests;

namespace EventStore.Projections.Core.Tests.Services.projections_system {
	namespace startup {
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_starting_with_empty_db<TLogFormat, TStreamId> : with_projections_subsystem<TLogFormat, TStreamId> {
			protected override IEnumerable<WhenStep> When() {
				yield return
					new ProjectionManagementMessage.Command.GetStatistics(Envelope, ProjectionMode.AllNonTransient,
						null, false)
					;
			}

			[Test]
			public void system_projections_are_registered() {
				var statistics = HandledMessages.OfType<ProjectionManagementMessage.Statistics>().LastOrDefault();
				Assert.NotNull(statistics);
				Assert.AreEqual(5, statistics.Projections.Length);
			}

			[Test]
			public void system_projections_are_running() {
				var statistics = HandledMessages.OfType<ProjectionManagementMessage.Statistics>().LastOrDefault();
				Assert.NotNull(statistics);
				Assert.That(statistics.Projections.All(s => s.Status == "Stopped"));
			}

			[Test]
			public void core_readers_should_use_the_unique_id_provided_by_the_component_start_message() {
				var startComponentMessages = _consumer.HandledMessages.OfType<ProjectionSubsystemMessage.StartComponents>().First();
				var startCoreMessages = _consumer.HandledMessages.OfType<ProjectionCoreServiceMessage.StartCore>();

				Assert.AreEqual(1, startCoreMessages.Select(x => x.InstanceCorrelationId).Distinct().Count());
				Assert.AreEqual(startComponentMessages.InstanceCorrelationId, startCoreMessages.First().InstanceCorrelationId);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_starting_as_follower<TLogFormat, TStreamId> : with_projections_subsystem<TLogFormat, TStreamId> {
			protected override IEnumerable<WhenStep> PreWhen() {
				yield return (new SystemMessage.BecomeFollower(Guid.NewGuid(),
					MemberInfo.Initial(Guid.NewGuid(), DateTime.UtcNow, VNodeState.Unknown, true,
						new IPEndPoint(IPAddress.Loopback, 1111),
						new IPEndPoint(IPAddress.Loopback, 1112),
						new IPEndPoint(IPAddress.Loopback, 1113),
						new IPEndPoint(IPAddress.Loopback, 1114),
						new IPEndPoint(IPAddress.Loopback, 1115), null, 0, 0,
						1,
						false
					)));
				yield return (new SystemMessage.SystemCoreReady());
				yield return Yield;
				if (_startSystemProjections) {
					yield return
						new ProjectionManagementMessage.Command.GetStatistics(Envelope, ProjectionMode.AllNonTransient,
							null, false)
						;
					var statistics = HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Last();
					foreach (var projection in statistics.Projections) {
						if (projection.Status != "Running")
							yield return
								new ProjectionManagementMessage.Command.Enable(
									Envelope, projection.Name, ProjectionManagementMessage.RunAs.Anonymous);
					}
				}
			}

			[Test]
			public void projections_core_coordinator_should_not_publish_start_core_message() {
				//projections are not allowed (yet) to run on followers
				var startCoreMessages = _consumer.HandledMessages.OfType<ProjectionCoreServiceMessage.StartCore>();
				Assert.AreEqual(0, startCoreMessages.Select(x => x.InstanceCorrelationId).Distinct().Count());
			}
		}
	}
}
