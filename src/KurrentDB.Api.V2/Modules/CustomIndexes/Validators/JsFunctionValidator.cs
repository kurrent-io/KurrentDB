// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;

namespace KurrentDB.Api.Modules.CustomIndexes.Validators;

internal static class JsFunctionValidator {
	private static readonly Engine Engine = new();

	public static bool IsValidFunctionWithOneArgument(string? jsFunction) {
		if (jsFunction is null)
			return false;

		try {
			lock (Engine) {
				var function = Engine.Evaluate(jsFunction).AsFunctionInstance();
				return function.FunctionDeclaration!.Params.Count == 1;
			}
		} catch {
			return false;
		}
	}
}
