// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// One change in the store's single migration stream — a class per step, a file per class.
/// The stream is append-only and versions only ever increment. A <see cref="MigrationStepType.RunOnce"/>
/// step's body is FROZEN the day it ships — it captures that moment's shape, and later steps
/// alter it; never edit an old one, append a new one. A <see cref="MigrationStepType.RunAlways"/>
/// step is the opposite: its body states the current desired form and reasserts it every boot.
///
/// Dependencies a step needs (config, options) arrive through its constructor; the execution
/// surface arrives per call, from the engine — most steps need nothing else.
/// </summary>
public interface IMigrationStep<in TContext> where TContext : class {
    /// <summary>The execution order. Unique across the stream; starts at 1. Registration order is irrelevant — the engine sorts.</summary>
    int Version { get; }

    /// <summary>Human identity for the plan, the logs, and the history rows. Defaults to the
    /// class name, minus any trailing "Task".</summary>
    string Name {
        get {
            var name = GetType().Name;
            return name.EndsWith("Task", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
    }

    /// <summary>Run once and skip forever (the default), or reassert on every boot.</summary>
    MigrationStepType Type => MigrationStepType.RunOnce;

    /// <summary>The change itself, run against the store's execution surface.</summary>
    Task ExecuteAsync(TContext context, CancellationToken ct = default);
}
