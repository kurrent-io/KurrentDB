// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// What a memory <em>is</em> — Kontext's trust axis. Whether a memory is firsthand or derived is NOT a
/// type: it is visible in <see cref="Memory.Evidence"/> (null ⇒ firsthand/asserted; populated ⇒ derived,
/// inheriting the certainty of the memories it cites). Trust and lifecycle are derived from
/// (type + evidence), not stored as separate axes.
/// </summary>
/// <remarks>
/// Grounded in Park et al. 2023 ("Generative Agents"); <see cref="Hearsay"/> and the durable-knowledge
/// types are deliberate extensions. Reflection is the process that produces the derived types — there is
/// deliberately no "reflection" type.
/// </remarks>
public enum MemoryType {
	/// <summary>Default / not set.</summary>
	Unspecified = 0,

	/// <summary>A perceived event — "what happened". Firsthand, so the highest trust. Episodic (decays).</summary>
	Observation = 1,

	/// <summary>
	/// A claim heard in dialogue — a naturally low-trust observation, kept unverified so a fabricated "fact"
	/// injected via conversation can't launder itself into truth (the "memory-hacking" guard). Episodic.
	/// </summary>
	Hearsay = 2,

	/// <summary>A durable truth about the world, a system, or a project. Persists; superseded, never decayed.</summary>
	Fact = 3,

	/// <summary>Durable how-to: a convention, workflow, or skill. Persists, superseded, not decayed.</summary>
	Procedure = 4,

	/// <summary>
	/// Durable facts about a principal — identity, role, and personal preferences (those binding only that
	/// principal). A "preference" binding a team/project is a <see cref="Procedure"/>/<see cref="Fact"/> at
	/// that scope, not a profile.
	/// </summary>
	Profile = 5,

	/// <summary>A future course of action. Time-bound — valid until it's acted on or it expires.</summary>
	Plan = 6,

	/// <summary>
	/// A consolidation of other memories — a recap, digest, or index. Always derived (cites its sources as
	/// evidence), so its certainty is inherited from them. Persists, refreshed as those sources change.
	/// </summary>
	Summary = 7,
}
