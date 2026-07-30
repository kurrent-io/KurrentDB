// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using Kurrent.Surge.Schema.Serializers.Json;
using KurrentDB.Scripting;
using static KurrentDB.Kontext.Tests.Fakes.FakeSystemClient;

namespace KurrentDB.Kontext.Tests.Indexing;

/// <summary>
/// Jint 4.14 changed the default for <c>Options.Interop.ArrayConversion</c> from <c>Copy</c> to
/// <c>LiveView</c>, and the two differ in ways a script can observe: a live view writes through to the CLR
/// array, <c>Array.isArray</c> is false for it, and resizing it throws a <c>TypeError</c>.
/// <para>
/// Whether that matters to us is a question about which values actually cross the interop boundary, and it
/// was previously answerable only by reading every call site. Jint 4.15.1 counts the conversions, so this
/// asserts the answer instead: mapping a record and running a filter and a field selector over it converts
/// no CLR array at all, under either mode.
/// </para>
/// <para>
/// Scope. This covers the scripting engines, which is where the question lives: <c>JsRecord</c> is the only
/// object graph this repository projects into script, so it is the only place a CLR array could appear. The
/// projections engine hands its handlers nothing but <c>JsValue</c>s and so cannot convert one. Note also
/// what the counters deliberately exclude: under <c>LiveView</c> an array crossing under a non-array
/// declared type (<c>IReadOnlyList&lt;T&gt;</c>) honours that declared contract through the ordinary wrapper
/// lane and is not counted -- so this asserts "no array conversion", not "no array-shaped value".
/// </para>
/// </summary>
public class InteropArrayConversionAuditTests {
	static (Engine Engine, JsRecordEvaluator Evaluator) Make() {
		var engine = JintEngineFactory.CreateEngine();
		return (engine, new JsRecordEvaluator(engine, SystemJsonSchemaSerializerOptions.Default));
	}

	[Test]
	public async Task Mapping_And_Filtering_A_Record_Converts_No_Clr_Array() {
		var (engine, evaluator) = Make();
		var filter = JsRecordEvaluator.Compile(engine, "rec => rec.value.org === 'acme'");
		var selector = JsRecordEvaluator.Compile(engine, "rec => rec.value.items.length");

		var evt = MakeEvent("ticket-1", 0, "TicketRaised", data: new { org = "acme", items = new[] { 1, 2, 3 } });
		evaluator.MapRecord(evt, sequence: 1);

		await Assert.That(evaluator.Match(filter)).IsTrue();
		await Assert.That(evaluator.Select(selector)!.AsNumber()).IsEqualTo(3d);

		var diagnostics = engine.Advanced.GetInteropConversionDiagnostics();
		await Assert.That(diagnostics.ArrayLiveViewConversions).IsEqualTo(0L);
		await Assert.That(diagnostics.ArrayCopyConversions).IsEqualTo(0L);
	}

	[Test]
	public async Task Reading_Every_Projected_Member_Converts_No_Clr_Array() {
		var (engine, evaluator) = Make();
		var selector = JsRecordEvaluator.Compile(
			engine,
			"""
			rec => [
			  rec.id, rec.sequence, rec.redacted, rec.timestamp,
			  rec.schema.name, rec.schema.format, rec.schema.id,
			  rec.position.stream, rec.position.streamRevision, rec.position.logPosition,
			  JSON.stringify(rec.value), JSON.stringify(rec.properties)
			].join('|')
			""");

		evaluator.MapRecord(MakeEvent("ticket-1", 0, "TicketRaised", data: new { org = "acme" }), sequence: 1);
		await Assert.That(evaluator.Select(selector)!.IsString()).IsTrue();

		var diagnostics = engine.Advanced.GetInteropConversionDiagnostics();
		await Assert.That(diagnostics.ArrayLiveViewConversions).IsEqualTo(0L);
		await Assert.That(diagnostics.ArrayCopyConversions).IsEqualTo(0L);
	}

	[Test]
	public async Task The_Counters_Would_Notice_An_Array_Crossing() {
		// Positive control: without this, the two assertions above would also pass if the counters were
		// never incremented at all.
		var engine = JintEngineFactory.CreateEngine();
		engine.SetValue("numbers", new[] { 1, 2, 3 });
		engine.Evaluate("numbers[0]");

		var diagnostics = engine.Advanced.GetInteropConversionDiagnostics();
		await Assert.That(diagnostics.ArrayLiveViewConversions).IsEqualTo(1L);
		await Assert.That(diagnostics.ArrayCopyConversions).IsEqualTo(0L);
	}
}
