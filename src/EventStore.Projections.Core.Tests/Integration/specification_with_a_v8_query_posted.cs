using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.projections_manager;

namespace EventStore.Projections.Core.Tests.Integration {
	public abstract class specification_with_a_v8_query_posted<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
		protected string _projectionName;
		protected string _projectionSource;
		protected ProjectionMode _projectionMode;
		protected bool _checkpointsEnabled;
		protected bool _trackEmittedStreams;
		protected bool _emitEnabled;
		protected bool _startSystemProjections;

		protected override void Given() {
			base.Given();
			AllWritesSucceed();
			NoOtherStreams();
			GivenEvents();
			EnableReadAll();
			_projectionName = "query";
			_projectionSource = GivenQuery();
			_projectionMode = ProjectionMode.Transient;
			_checkpointsEnabled = false;
			_trackEmittedStreams = false;
			_emitEnabled = false;
			_startSystemProjections = GivenStartSystemProjections();
		}

		protected override Tuple<IBus, IPublisher, InMemoryBus, TimeoutScheduler, Guid>[] GivenProcessingQueues() {
			var buses = new IBus[] { new InMemoryBus("1"), new InMemoryBus("2") };
			var outBuses = new[] { new InMemoryBus("o1"), new InMemoryBus("o2") };
			_otherQueues = new ManualQueue[]
				{new ManualQueue(buses[0], _timeProvider), new ManualQueue(buses[1], _timeProvider)};
			return new[] {
				Tuple.Create(
					buses[0],
					(IPublisher)_otherQueues[0],
					outBuses[0],
					default(TimeoutScheduler),
					Guid.NewGuid()),
				Tuple.Create(
					buses[1],
					(IPublisher)_otherQueues[1],
					outBuses[1],
					default(TimeoutScheduler),
					Guid.NewGuid())
			};
		}

		protected abstract void GivenEvents();

		protected abstract string GivenQuery();

		protected virtual bool GivenStartSystemProjections() {
			return false;
		}

		protected Message CreateQueryMessage(string name, string source) {
			return new ProjectionManagementMessage.Command.Post(
				new PublishEnvelope(_bus), ProjectionMode.Transient, name,
				ProjectionManagementMessage.RunAs.System, "JS", source, enabled: true, checkpointsEnabled: false,
				trackEmittedStreams: false,
				emitEnabled: false);
		}

		protected Message CreateNewProjectionMessage(string name, string source) {
			return new ProjectionManagementMessage.Command.Post(
				new PublishEnvelope(_bus), ProjectionMode.Continuous, name, ProjectionManagementMessage.RunAs.System,
				"JS", source, enabled: true, checkpointsEnabled: true, trackEmittedStreams: true, emitEnabled: true);
		}

		protected override IEnumerable<WhenStep> When() {
			yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
			if (_startSystemProjections) {
				yield return
					new ProjectionManagementMessage.Command.Enable(
						Envelope, ProjectionNamesBuilder.StandardProjections.StreamsStandardProjection,
						ProjectionManagementMessage.RunAs.System);
				yield return
					new ProjectionManagementMessage.Command.Enable(
						Envelope, ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection,
						ProjectionManagementMessage.RunAs.System);
				yield return
					new ProjectionManagementMessage.Command.Enable(
						Envelope, ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection,
						ProjectionManagementMessage.RunAs.System);
				yield return
					new ProjectionManagementMessage.Command.Enable(
						Envelope, ProjectionNamesBuilder.StandardProjections.EventByTypeStandardProjection,
						ProjectionManagementMessage.RunAs.System);
			}

			var otherProjections = GivenOtherProjections();
			var index = 0;
			foreach (var source in otherProjections) {
				yield return
					(new ProjectionManagementMessage.Command.Post(
						new PublishEnvelope(_bus), ProjectionMode.Continuous, "other_" + index,
						ProjectionManagementMessage.RunAs.System, "JS", source, enabled: true, checkpointsEnabled: true,
						trackEmittedStreams: true,
						emitEnabled: true));
				index++;
			}

			if (!string.IsNullOrEmpty(_projectionSource)) {
				yield return
					(new ProjectionManagementMessage.Command.Post(
						new PublishEnvelope(_bus), _projectionMode, _projectionName,
						ProjectionManagementMessage.RunAs.System, "JS", _projectionSource, enabled: true,
						checkpointsEnabled: _checkpointsEnabled, emitEnabled: _emitEnabled,
						trackEmittedStreams: _trackEmittedStreams));
			}
		}

		protected virtual IEnumerable<string> GivenOtherProjections() {
			return new string[0];
		}
	}
}
