// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using Jint.Native;
using Jint.Native.Function;
using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

public class JsFunctionCallMeasurer(IProjectionExecutionTracker tracker) {
	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction) {
		var start = Instant.Now;
		var result = jsFunction.Call();

		tracker.CallExecuted(start, jsFunctionName);

		return result;
	}

	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1) {
		var start = Instant.Now;
		var result = jsFunction.Call(jsArg1);

		tracker.CallExecuted(start, jsFunctionName);

		return result;
	}

	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1, JsValue jsArg2) {
		var start = Instant.Now;
		var result = jsFunction.Call(jsArg1, jsArg2);

		tracker.CallExecuted(start, jsFunctionName);

		return result;
	}

	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction, params JsValue[] jsArgs) {
		var start = Instant.Now;
		var result = jsFunction.Call(jsArgs);

		tracker.CallExecuted(start, jsFunctionName);

		return result;
	}
}
