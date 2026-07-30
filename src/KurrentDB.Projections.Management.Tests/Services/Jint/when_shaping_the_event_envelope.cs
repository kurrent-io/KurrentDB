// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using Jint;
using Jint.Native.Json;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Interpreted;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

/// <summary>
/// The reason the envelope is built from a layout rather than assembled property by property is that
/// every envelope of a variant then shares one hidden class, which is what keeps a handler's member
/// reads monomorphic across events. That is invisible from script -- the representations behave
/// identically and differ only in speed -- so without asking the engine directly it can only ever be
/// observed once, by hand, and then silently lost.
/// <para>
/// <c>JsObject.Create</c> falls back to the ordinary property dictionary quietly and correctly when it
/// cannot shape an object, and two of the triggers depend on what the engine has already built rather
/// than on anything visible at the call site. This asserts the fallback did not happen.
/// </para>
/// </summary>
[TestFixture]
public class when_shaping_the_event_envelope {
	private static ResolvedEvent Event(bool isJson) => new(
		positionStreamId: "test-stream",
		positionSequenceNumber: 1,
		eventStreamId: "test-stream",
		eventSequenceNumber: 1,
		resolvedLinkTo: false,
		position: new TFPos(100, 50),
		eventOrLinkTargetPosition: new TFPos(100, 50),
		eventId: Guid.NewGuid(),
		eventType: "TestEvent",
		isJson: isJson,
		data: Encoding.UTF8.GetBytes(isJson ? """{"the-key":"the-value"}""" : "this is not json"),
		metadata: Encoding.UTF8.GetBytes("""{"the-meta-key":"the-meta-value"}"""),
		positionMetadata: null,
		streamMetadata: null,
		timestamp: new DateTime(2023, 4, 5, 12, 34, 56, DateTimeKind.Utc));

	private static JintProjectionStateHandler.EventEnvelope Create(Engine engine, bool isJson) =>
		new(engine, new JsonParser(engine), "test-partition", Event(isJson), "test-category");

	[Test, Category("js")]
	public void both_layout_variants_produce_shaped_objects() {
		var engine = new Engine();

		Assert.IsTrue(
			engine.Advanced.HasSharedShape(Create(engine, isJson: true).Value),
			"the JSON envelope variant fell back to the property dictionary");
		Assert.IsTrue(
			engine.Advanced.HasSharedShape(Create(engine, isJson: false).Value),
			"the non-JSON envelope variant fell back to the property dictionary");
	}

	[Test, Category("js")]
	public void envelopes_of_one_variant_share_their_shape_across_events() {
		// The point of the layout is cross-event sharing, so a second envelope built from the same variant
		// must be shaped too -- one shaped object would prove nothing about the handler's steady state.
		var engine = new Engine();

		for (var i = 0; i < 5; i++) {
			Assert.IsTrue(engine.Advanced.HasSharedShape(Create(engine, isJson: true).Value));
		}
	}
}
