#pragma warning disable IDE0073 // The file header does not match the required text
//----------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//----------------------------------------------------------------

namespace System.ServiceModel;

internal sealed class DiagnosticUtility {
	internal sealed class ExceptionUtility {
		internal static Exception ThrowHelperArgumentNull(string paramName) {
			throw new ArgumentNullException(paramName);
		}

		internal static Exception ThrowHelperArgument(string paramName, string message) {
			throw new ArgumentException(message, paramName);
		}

		internal static Exception ThrowHelperError(Exception exception) {
			throw exception;
		}
	}
}
