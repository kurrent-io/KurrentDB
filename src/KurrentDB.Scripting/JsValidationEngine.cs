// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Jint;

namespace KurrentDB.Scripting;

/// <summary>
/// A long-lived engine used to compile and check user-supplied JavaScript before it is stored.
/// Validation evaluates arbitrary source on an engine that outlives every request, which needs two
/// things to be true: only one validation at a time, because a Jint engine is not thread safe, and
/// no validation leaving anything behind for the next one.
/// <para>
/// The second part is not automatic. Evaluating an expression can define globals -- <c>(g = 1,
/// function (r) { ... })</c> both validates and leaks -- and on a process-lifetime engine those
/// accumulate forever and are visible to every later validation. Jint's global snapshot is the
/// primitive for it: capture the configured global surface once at construction, restore it after
/// each use.
/// </para>
/// <para>
/// It reverses global bindings, not everything a script can do: mutations of built-in prototypes
/// survive it, so this bounds accidental accumulation rather than making hostile input safe.
/// </para>
/// </summary>
public sealed class JsValidationEngine {
	readonly Engine _engine;
	readonly GlobalSnapshot _snapshot;

	/// <param name="engine">
	/// The engine to validate on, already configured. Ownership transfers: nothing else may use it,
	/// since a restore would roll back whatever another caller had just set up.
	/// </param>
	public JsValidationEngine(Engine engine) {
		_engine = engine;
		_snapshot = engine.Advanced.CaptureGlobalSnapshot();
	}

	/// <summary>
	/// Runs <paramref name="validate"/> against the engine under a lock, then returns the engine's
	/// globals to the state they were captured in, whether or not it threw.
	/// </summary>
	/// <remarks>
	/// The restore is Jint's own <c>WithRestoredGlobals</c> rather than a hand-written try/finally. The
	/// lock stays ours: an engine is single-threaded, and that helper is a finally, not a sandbox.
	/// </remarks>
	public T Validate<T>(Func<Engine, T> validate) {
		lock (_engine) {
			var result = default(T)!;
			_engine.Advanced.WithRestoredGlobals(_snapshot, () => result = validate(_engine));
			return result;
		}
	}
}
