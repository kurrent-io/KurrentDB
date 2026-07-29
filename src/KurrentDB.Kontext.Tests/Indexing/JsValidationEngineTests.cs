// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using KurrentDB.Scripting;

namespace KurrentDB.Kontext.Tests.Indexing;

public class JsValidationEngineTests {
	[Test]
	public async Task Globals_Defined_By_One_Validation_Are_Not_Visible_To_The_Next() {
		var engine = new JsValidationEngine(JintEngineFactory.CreateEngine());

		// A filter expression is evaluated as `(<expr>)`, and a comma expression lets one both define a
		// global and produce the function that passes validation. On a shared, process-lifetime engine
		// that global would otherwise outlive the request that created it.
		engine.Validate(e => e.Evaluate("(globalThis.leaked = 1, r => true)"));

		var stillThere = engine.Validate(e => e.Evaluate("typeof globalThis.leaked").AsString());
		await Assert.That(stillThere).IsEqualTo("undefined");
	}

	[Test]
	public async Task Globals_Are_Restored_Even_When_A_Validation_Throws() {
		var engine = new JsValidationEngine(JintEngineFactory.CreateEngine());

		Assert.Throws<Exception>(() => engine.Validate<object>(e => {
			e.Evaluate("globalThis.leaked = 1");
			throw new InvalidOperationException("rejected");
		}));

		var stillThere = engine.Validate(e => e.Evaluate("typeof globalThis.leaked").AsString());
		await Assert.That(stillThere).IsEqualTo("undefined");
	}

	[Test]
	public async Task Host_Configuration_Captured_At_Construction_Survives_A_Restore() {
		var underlying = JintEngineFactory.CreateEngine();
		underlying.SetValue("hostProvided", 42);
		var engine = new JsValidationEngine(underlying);

		engine.Validate(e => e.Evaluate("(globalThis.leaked = 1, r => true)"));

		var stillThere = engine.Validate(e => e.Evaluate("hostProvided").AsNumber());
		await Assert.That(stillThere).IsEqualTo(42d);
	}
}
