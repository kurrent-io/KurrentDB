using System;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.Jint {
	[TestFixture]
	public class when_js_projection_loading_state : TestFixtureWithInterpretedProjection {
		protected override void Given() {
			_projection = @"
                fromAll().when({$any: 
                    function(state, event) {
                        return state;
                    }
                });
            ";
			_state = @"{""A"":""A"",""B"":""B""}";
		}

		[Test, Category(_projectionType)]
		public void the_state_is_loaded() {
			string state;
			EmittedEventEnvelope[] emittedEvents;
			_stateHandler.ProcessEvent(
				"", CheckpointTag.FromPosition(0, 20, 10), "stream1", "type1", "category", Guid.NewGuid(), 0,
				"metadata",
				@"{""x"":""y""}", out state, out emittedEvents);

			Assert.AreEqual(_state, state);
		}
	}
}