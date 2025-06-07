// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

//qq move, no longer part of metrics
public class JsFunctionCallMeasurer(IProjectionExecutionTracker tracker) {
	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction) {
		using var _ = new Measurer(tracker, jsFunctionName);
		return jsFunction.Call();
	}

	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1) {
		using var _ = new Measurer(tracker, jsFunctionName);
		return jsFunction.Call(jsArg1);
	}

	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1, JsValue jsArg2) {
		using var _ = new Measurer(tracker, jsFunctionName);
		return jsFunction.Call(jsArg1, jsArg2);
	}

	public JsValue Call(string jsFunctionName, ScriptFunction jsFunction, params JsValue[] jsArgs) {
		using var _ = new Measurer(tracker, jsFunctionName);
		return jsFunction.Call(jsArgs);
	}

	readonly struct Measurer(IProjectionExecutionTracker tracker, string jsFunctionName) : IDisposable {
		readonly Instant _start = Instant.Now;

		public void Dispose() {
			tracker.CallExecuted(_start, jsFunctionName);
		}
	}
}
