// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint;

[TestFixture]
public class when_defining_a_js_projection {
	private const string _projectionType = "INTERPRETED";
	[TestFixture]
	public class with_from_all_source : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                   fromAll().when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.FromAll();
				b.AllEvents();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_from_stream : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromStream('stream1').when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromStream("stream1");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_multiple_from_streams : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromStreams(['stream1', 'stream2', 'stream3']).when({
                    $any: function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromStream("stream1");
				b.FromStream("stream2");
				b.FromStream("stream3");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_multiple_from_streams_plain : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromStreams('stream1', 'stream2', 'stream3').when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromStream("stream1");
				b.FromStream("stream2");
				b.FromStream("stream3");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_multiple_from_categories : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromCategories(['category1', 'category2', 'category3']).when({
                    $any: function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromStream("$ce-category1");
				b.FromStream("$ce-category2");
				b.FromStream("$ce-category3");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_multiple_from_categories_plain : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromCategories('category1', 'category2', 'category3').when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromStream("$ce-category1");
				b.FromStream("$ce-category2");
				b.FromStream("$ce-category3");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_from_category : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromCategory('category1').when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromCategory("category1");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_from_category_by_stream : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromCategory('category1').foreachStream().when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromCategory("category1");
				b.SetByStream();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_from_all_by_custom_partitions : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromAll().partitionBy(function(event){
                        return event.eventType;
                    }).when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetByCustomPartitions();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_output_to : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromAll()
                    .when({
                        $any:function(state, event) {
                            return state;
                        }})
                    .$defines_state_transform();
                ";
			_state = @"{""count"": 0}";
		}

		[Test]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetDefinesStateTransform();
				b.SetOutputState();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_transform_by : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromAll().when({
                        $any:function(state, event) {
                            return state;
                        }}).transformBy(function(s) {return s;});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetDefinesStateTransform();
				b.SetOutputState();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	public class with_filter_by : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    fromAll().when({
                        some: function(state, event) {
                            return state;
                        }
                    }).filterBy(function(s) {return true;});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.FromAll();
				b.IncludeEvent("some");
				b.SetDefinesStateTransform();
				b.SetOutputState();
			});
			AssertEx.AreEqual(expected, _source);
		}
	}

	public class with_output_state : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    var z = fromAll().outputState();
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.NoWhen();
				b.SetOutputState();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	public class without_when : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    var z = fromAll();
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.NoWhen();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	public class with_when : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    var z = fromAll().when({a: function(s,e) {}});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.FromAll();
				b.IncludeEvent("a");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_state_stream_name_option : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    options({
                        resultStreamName: 'state-stream',
                    });
                    fromAll().when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetResultStreamNameOption("state-stream");
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_include_links_option : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    options({
                        $includeLinks: true,
                    });
                    fromAll().when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetIncludeLinks();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_bi_state_option : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    options({
                        biState: true,
                    });
                    fromAll().when({
                        $any:function(state, sharedState, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetDefinesFold();
				b.SetIsBiState(true);
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_reorder_events_option : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    options({
                        reorderEvents: true,
                        processingLag: 500,
                    });
                    fromAll().when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetProcessingLag(500);
				b.SetReorderEvents(true);
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_multiple_option_statements : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                    options({
                        reorderEvents: false,
                        processingLag: 500,
                    });
                    options({
                        reorderEvents: true,
                    });
                    fromAll().when({
                        $any:function(state, event) {
                            return state;
                        }});
                ";
			_state = @"{""count"": 0}";
		}

		[Test, Category(_projectionType)]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetProcessingLag(500);
				b.SetReorderEvents(true);
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_foreach_and_deleted_notification_handled : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"fromAll().foreachStream().when({
                $deleted: function(){}
            })";
			_state = @"{}";
		}

		[Test]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetByStream();
				b.SetHandlesStreamDeletedNotifications();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}

	[TestFixture]
	public class with_deleted_notification_handled : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"fromAll().when({
                $deleted: function(){}
            })";
			_state = @"{}";
		}

		[Test]
		public void source_definition_is_correct() {
			var expected = SourceDefinitionBuilder.From(b => {
				b.AllEvents();
				b.FromAll();
				b.SetHandlesStreamDeletedNotifications();
			});
			AssertEx.AreEqual(expected,_source);
		}
	}
}
