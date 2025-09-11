// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Testing.TUnit;

public static class TestContextExtensions {
	/// <summary>
	/// Extracts the method name from the TestId, removing any parameters or additional info.
	/// Example: "Namespace.ClassName.MethodName: Some additional info" -> "MethodName"
	/// </summary>
	public static string GetTestMethodName(this TestContext ctx) {
		// Get the last segment after splitting by '.'
		var methodNameWithPossibleParams = ctx.TestDetails.TestId.Split('.').Last();
		// Remove any parameters or additional info after ':'
		var colonIndex = methodNameWithPossibleParams.IndexOf(':');
		return colonIndex >= 0
			? methodNameWithPossibleParams[..colonIndex]
			: methodNameWithPossibleParams;
	}
}
