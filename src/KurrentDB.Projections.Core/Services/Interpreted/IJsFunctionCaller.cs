// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using Jint.Native;
using Jint.Native.Function;

namespace KurrentDB.Projections.Core.Services.Interpreted;

public interface IJsFunctionCaller {
	public static readonly IJsFunctionCaller Default = DefaultJsFunctionCaller.Instance;

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction);
	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1);
	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1, JsValue jsArg2);
	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, params JsValue[] jsArgs);
}

file sealed class DefaultJsFunctionCaller : IJsFunctionCaller {
	public static readonly DefaultJsFunctionCaller Instance = new();

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction) =>
		jsFunction.Call();

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1) =>
		jsFunction.Call(jsArg1);

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, JsValue jsArg1, JsValue jsArg2) =>
		jsFunction.Call(jsArg1, jsArg2);

	public JsValue Call(string projectionName, string jsFunctionName, ScriptFunction jsFunction, params JsValue[] jsArgs) =>
		jsFunction.Call(jsArgs);
}
