// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using System.Linq;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;
using EventStore.Common.Utils;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using Newtonsoft.Json.Linq;

namespace EventStore.Projections.Core.Tests.Services.projections_system.updating_projections {
	namespace updating_projection_sources {
		public abstract class with_updated_projection<TLogFormat, TStreamId> : with_projection_config<TLogFormat, TStreamId> {
			private ProjectionManagementMessage.Statistics _allStatistics;
			protected ProjectionManagementMessage.ProjectionState _state;
			private ProjectionManagementMessage.ProjectionQuery _query;
			private ProjectionStatistics _statistics;
			protected JObject _stateData;

			protected abstract string GivenOriginalSource();
			protected abstract string GivenUpdatedSource();

			protected override bool GivenStartSystemProjections() {
				return true;
			}

			protected override IEnumerable<WhenStep> When() {
				yield return CreateWriteEvent("stream1", "type1", "{\"Data\": 1}");
				yield return
					new ProjectionManagementMessage.Command.Post(
						Envelope, ProjectionMode.Continuous, _projectionName,
						ProjectionManagementMessage.RunAs.System, "js", GivenOriginalSource(), true,
						_checkpointsEnabled, _emitEnabled, _trackEmittedStreams);
				yield return CreateWriteEvent("stream1", "type2", "{\"Data\": 2}");
				yield return CreateWriteEvent("stream2", "type2", "{\"Data\": 3}");
				yield return CreateWriteEvent("stream3", "type3", "{\"Data\": 4}");
				yield return CreateWriteEvent("stream3", "type1", "{\"Data\": 5}");
				yield return
					new ProjectionManagementMessage.Command.Disable(
						Envelope, _projectionName, ProjectionManagementMessage.RunAs.System);
				yield return
					new ProjectionManagementMessage.Command.UpdateQuery(
						Envelope, _projectionName, ProjectionManagementMessage.RunAs.System,
						GivenUpdatedSource(), _emitEnabled);
				yield return CreateWriteEvent("stream2", "type3", "{\"Data\": 6}");
				yield return CreateWriteEvent("stream3", "type4", "{\"Data\": 7}");
				yield return
					new ProjectionManagementMessage.Command.Enable(
						Envelope, _projectionName, ProjectionManagementMessage.RunAs.System);
				yield return CreateWriteEvent("stream3", "type4", "{\"Data\": 8}");
				yield return CreateWriteEvent("stream4", "type5", "{\"Data\": 9}");
				yield return CreateWriteEvent("stream5", "type1", "{\"Data\": 10}");
				yield return
					new ProjectionManagementMessage.Command.GetStatistics(
						Envelope, ProjectionMode.AllNonTransient, _projectionName, false);
				yield return new ProjectionManagementMessage.Command.GetState(Envelope, _projectionName, "");
				yield return
					new ProjectionManagementMessage.Command.GetQuery(
						Envelope, _projectionName, ProjectionManagementMessage.RunAs.Anonymous);

				_allStatistics = HandledMessages.OfType<ProjectionManagementMessage.Statistics>().LastOrDefault();
				_statistics = _allStatistics != null ? _allStatistics.Projections.SingleOrDefault() : null;

				_state = HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().LastOrDefault();
				_stateData = _state != null ? EatException(() => _state.State.ParseJson<JObject>()) : null;
				_query = HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().LastOrDefault();
			}

			[Test]
			public void status_is_running() {
				Assert.NotNull(_statistics);
				Assert.AreEqual("Running", _statistics.Status);
			}

			[Test]
			public void query_test_is_updated() {
				Assert.NotNull(_query);
				Assert.AreEqual(GivenUpdatedSource(), _query.Query);
			}

			[Test]
			public void projection_state_can_be_retrieved() {
				Assert.NotNull(_state);
				Assert.NotNull(_stateData);
				Console.WriteLine(_stateData.ToJson());
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_adding_an_event_type<TLogFormat, TStreamId> : with_updated_projection<TLogFormat, TStreamId> {
			protected override string GivenOriginalSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        type1: handle,
                    });
                ";
			}

			protected override string GivenUpdatedSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        type1: handle,
                        type3: handle,
                    });
                ";
			}

			[Test]
			public void correct_event_sequence_has_been_processed() {
				HelperExtensions.AssertJson(new {d = new[] {1, 5, 6}}, _stateData);
			}

			[Test]
			public void projection_position_is_correct() {
				var pos = GetTfPos("stream5", 0);
				Assert.AreEqual(
					CheckpointTag.FromEventTypeIndexPositions(0, pos,
						new Dictionary<string, long> {{"type1", 1}, {"type3", 1}}), _state.Position);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_replacing_an_event_type<TLogFormat, TStreamId> : with_updated_projection<TLogFormat, TStreamId> {
			protected override string GivenOriginalSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        type1: handle,
                        type2: handle,
                    });
                ";
			}

			protected override string GivenUpdatedSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        type1: handle,
                        type3: handle,
                    });
                ";
			}

			[Test]
			public void correct_event_sequence_has_been_processed() {
				HelperExtensions.AssertJson(new {d = new[] {1, 2, 3, 5, 6, 10}}, _stateData);
			}

			[Test]
			public void projection_position_is_correct() {
				var pos = GetTfPos("stream5", 0);
				Assert.AreEqual(
					CheckpointTag.FromEventTypeIndexPositions(0, pos,
						new Dictionary<string, long> {{"type1", 1}, {"type3", 1}}), _state.Position);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_replacing_any_with_an_event_type<TLogFormat, TStreamId> : with_updated_projection<TLogFormat, TStreamId> {
			protected override string GivenOriginalSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			protected override string GivenUpdatedSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        type3: handle,
                    });
                ";
			}

			[Test]
			public void correct_event_sequence_has_been_processed() {
				HelperExtensions.AssertJson(new {d = new[] {1, 2, 3, 4, 5, 6}}, _stateData);
			}

			[Test]
			public void projection_position_is_correct() {
				var pos = GetTfPos("stream2", 1);
				Assert.That(
					CheckpointTag.FromEventTypeIndexPositions(0, pos, new Dictionary<string, long> {{"type3", 1}}) <=
					_state.Position);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_replacing_specific_event_types_with_any<TLogFormat, TStreamId> : with_updated_projection<TLogFormat, TStreamId> {
			protected override string GivenOriginalSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        type1: handle,
                        type2: handle,
                    });
                ";
			}

			protected override string GivenUpdatedSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromAll().when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			[Test]
			public void correct_event_sequence_has_been_processed() {
				HelperExtensions.AssertJson(new {d = new[] {1, 2, 3, 5, 6, 7, 8, 9, 10}}, _stateData);
			}

			[Test]
			public void projection_position_is_correct() {
				var pos = GetTfPos("stream5", 0);
				Assert.AreEqual(CheckpointTag.FromPosition(0, pos.CommitPosition, pos.PreparePosition),
					_state.Position);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_replacing_stream_with_multiple_streams<TLogFormat, TStreamId> : with_updated_projection<TLogFormat, TStreamId> {
			protected override string GivenOriginalSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromStream('stream1').when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			protected override string GivenUpdatedSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromStreams('stream1', 'stream2').when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			[Test, Ignore("No position with stream tag yet")]
			public void correct_event_sequence_has_been_processed() {
				HelperExtensions.AssertJson(new {d = new[] {1, 2, 6}}, _stateData);
			}

			[Test]
			public void projection_position_is_correct() {
				Assert.AreEqual(
					CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"stream1", 1}, {"stream2", 1}}),
					_state.Position);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_replacing_multiple_streams_with_one_of_them<TLogFormat, TStreamId> : with_updated_projection<TLogFormat, TStreamId> {
			protected override string GivenOriginalSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromStreams('stream1', 'stream2').when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			protected override string GivenUpdatedSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromStream('stream2').when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			[Test]
			public void correct_event_sequence_has_been_processed() {
				HelperExtensions.AssertJson(new {d = new[] {1, 2, 3, 6}}, _stateData);
			}

			[Test]
			public void projection_position_is_correct() {
				Assert.AreEqual(CheckpointTag.FromStreamPosition(0, "stream2", 1), _state.Position);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class when_replacing_a_stream_in_multiple_streams<TLogFormat, TStreamId> : with_updated_projection<TLogFormat, TStreamId> {
			protected override string GivenOriginalSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromStreams('stream1', 'stream2').when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			protected override string GivenUpdatedSource() {
				return @"
                    function handle(s, e) { if (e.data && e.data.Data) s.d.push(e.data.Data); return s; }
                    fromStreams('stream2', 'stream3').when({
                        $init: function(){return {d:[]};},
                        $any: handle,
                    });
                ";
			}

			[Test, Ignore("No position in multi-stream tag")]
			public void correct_event_sequence_has_been_processed() {
				HelperExtensions.AssertJson(new {d = new[] {1, 2, 3, 6, 7, 8}}, _stateData);
			}

			[Test]
			public void projection_position_is_correct() {
				Assert.AreEqual(
					CheckpointTag.FromStreamPositions(0, new Dictionary<string, long> {{"stream2", 1}, {"stream3", 3}}),
					_state.Position);
			}
		}
	}
}
