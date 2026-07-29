// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;
using KurrentDB.Scripting;

namespace KurrentDB.Api.Modules.Indexes.Validators;

internal static class JsFunctionValidator {
	// The engine outlives every request and evaluates user-supplied source, so each validation has its
	// globals rolled back afterwards rather than leaving them for the next one. The engine's own
	// configuration is unchanged.
	private static readonly JsValidationEngine Engine = new(new Engine());

	public static bool IsValidFunctionWithOneArgument(string? jsFunction) {
		if (jsFunction is null)
			return false;

		try {
			return Engine.Validate(engine => {
				var function = engine.Evaluate(jsFunction).AsFunctionInstance();
				return function.FunctionDeclaration!.Params.Count == 1;
			});
		} catch {
			return false;
		}
	}
}
