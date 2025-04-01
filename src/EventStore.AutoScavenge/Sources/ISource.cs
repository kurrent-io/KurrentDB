// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge.Sources;

/// <summary>
/// Event provider abstraction use to source events to the auto scavenge state machine.
/// </summary>
public interface ISource {
	/// <summary>
	/// Reads the last auto scavenge configuration event.
	/// </summary>
	/// <param name="token"></param>
	/// <returns></returns>
	Task<Events.ConfigurationUpdated?> ReadConfigurationEvent(CancellationToken token);

	/// <summary>
	/// Reads the events necessary to initialize the auto scavenge state machine.
	/// </summary>
	/// <param name="token"></param>
	/// <returns>Ascending ordered collection of events related to the auto scavenge process</returns>
	IAsyncEnumerable<IEvent> ReadAutoScavengeEvents(CancellationToken token);
}
