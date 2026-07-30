// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Jint;

internal static class JintHostContractVerification {
	/// <summary>
	/// Turns on Jint's host-contract verifiers for this test assembly.
	/// </summary>
	/// <remarks>
	/// These are the checks that catch host code answering one of the engine's extension points in a way
	/// that contradicts another -- a key that vanishes from every enumeration, a read that resolves on the
	/// prototype for a property that exists. The engine trusts those hooks on its hot paths and cannot
	/// re-verify them there, so a violation is otherwise silent.
	/// <para>
	/// The switch has to be set before the first use of any Jint type: the flag behind it is read once at
	/// type initialization, and flipping it afterwards does nothing for the rest of the process. A module
	/// initializer runs before any test in the assembly, which is early enough. There is no cost to leaving
	/// it off in production -- the guards fold away entirely when the switch is unset -- and it is
	/// deliberately not set anywhere but here.
	/// </para>
	/// </remarks>
	[ModuleInitializer]
	internal static void Enable() => AppContext.SetSwitch("Jint.EnableHostContractVerification", true);
}

/// <summary>
/// Positive control for the switch above. Without it, every suite in this assembly would report exactly
/// the same green whether the verifiers were running or silently disabled -- and a switch that has to be
/// set before the first use of any Jint type is easy to render inert by accident.
/// </summary>
[TestFixture]
public class when_a_host_object_violates_its_read_contract {
	/// <summary>
	/// Claims every name as its own while <c>GetOwnProperty</c> reports them all absent, which is the
	/// contradiction <c>TryGetOwnPropertyValue</c>'s contract forbids.
	/// </summary>
	private sealed class LyingHostObject(Engine engine) : ObjectInstance(engine) {
		protected override bool TryGetOwnPropertyValue(JsValue property, JsValue receiver, out JsValue value) {
			value = JsValue.Null;
			return true;
		}
	}

	[Test, Category("js")]
	public void the_verifiers_are_enabled_in_this_assembly() {
		var engine = new Engine();
		engine.SetValue("host", new LyingHostObject(engine));

		var ex = Assert.Throws<InvalidOperationException>(() => engine.Evaluate("host.anything"));
		StringAssert.Contains("TryGetOwnPropertyValue", ex.Message);
	}
}
