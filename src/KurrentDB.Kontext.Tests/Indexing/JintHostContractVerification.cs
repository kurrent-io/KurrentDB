// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Jint.Native.Object;

namespace KurrentDB.Kontext.Tests.Indexing;

internal static class JintHostContractVerification {
	/// <summary>
	/// Turns on Jint's host-contract verifiers for this test assembly.
	/// </summary>
	/// <remarks>
	/// These are the checks that catch host code answering one of the engine's extension points in a way
	/// that contradicts another. The engine trusts those hooks on its hot paths and cannot re-verify them
	/// there, so a violation is otherwise silent.
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
/// Positive control for the switch above. The scripting engines have no host <c>ObjectInstance</c>
/// subclass today, so the verifiers currently guard against a future one rather than anything present --
/// which makes it all the more worth proving the switch is actually live, since nothing else in this
/// assembly would notice if it were not.
/// </summary>
public class JintHostContractVerificationTests {
	/// <summary>
	/// Claims every name as its own while <c>GetOwnProperty</c> reports them all absent, which is the
	/// contradiction <c>TryGetOwnPropertyValue</c>'s contract forbids.
	/// </summary>
	sealed class LyingHostObject(Engine engine) : ObjectInstance(engine) {
		protected override bool TryGetOwnPropertyValue(JsValue property, JsValue receiver, out JsValue value) {
			value = JsValue.Null;
			return true;
		}
	}

	[Test]
	public async Task The_verifiers_are_enabled_in_this_assembly() {
		var engine = new Engine();
		engine.SetValue("host", new LyingHostObject(engine));

		var ex = Assert.Throws<InvalidOperationException>(() => engine.Evaluate("host.anything"));
		await Assert.That(ex!.Message).Contains("TryGetOwnPropertyValue");
	}
}
