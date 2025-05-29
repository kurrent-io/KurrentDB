// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Metrics;

public class JsFunctionCallMeasurer(IDurationMetric durationMetric) : IJsFunctionCaller {
	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction) {
		var start = Instant.Now;
		JsValue result = jsFunction.Call();

		RecordDuration(start, projectionName, jsFunctionName);

		return result;
	}

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1) {
		var start = Instant.Now;
		JsValue result = jsFunction.Call(jsArg1);

		RecordDuration(start, projectionName, jsFunctionName);

		return result;
	}

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1, JsValue jsArg2) {
		var start = Instant.Now;
		JsValue result = jsFunction.Call(jsArg1, jsArg2);

		RecordDuration(start, projectionName, jsFunctionName);

		return result;
	}

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, params JsValue[] jsArgs) {
		var start = Instant.Now;
		JsValue result = jsFunction.Call(jsArgs);

		RecordDuration(start, projectionName, jsFunctionName);

		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void RecordDuration(Instant start, string projectionName, string jsFunctionName) {
		durationMetric.Record(start,
			new KeyValuePair<string, object>("projection", projectionName),
			new KeyValuePair<string, object>("jsFunction", jsFunctionName));
	}
}
