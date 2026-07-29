// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using Jint;

namespace KurrentDB.Scripting;

public static class JintEngineFactory {
	static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	public static Engine CreateEngine(TimeSpan? executionTimeout = null) {
		var timeout = executionTimeout ?? DefaultTimeout;

		return new Engine(options => {
			options
				.Strict()
				.Culture(CultureInfo.InvariantCulture)
				.DisableStringCompilation()
				.TimeoutInterval(timeout);

			// Renders CLR enums as their member name rather than the underlying number, which is what the
			// scripts expect of record.schema.format. This replaces a hand-written IObjectConverter that did
			// exactly the same thing (Enum.GetName(...) ?? e.ToString(), which is what Jint's String mode
			// produces): registering any object converter makes engine-wide interop reads fall back to
			// reflection + boxing, because a converter must be offered every CLR value before it becomes a
			// JsValue, so one converter for one enum cost the compiled member-read lane for every property
			// and field read on every wrapped object in the engine.
			options.Interop.EnumConversion = EnumConversionMode.String;
		});
	}
}
