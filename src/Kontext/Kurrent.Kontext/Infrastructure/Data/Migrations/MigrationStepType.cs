// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

public enum MigrationStepType {
    /// <summary>
    /// Runs once, is recorded, and is skipped forever after. The default. Its body is FROZEN
    /// the day it ships — it captures that moment's shape, and later steps alter it.
    /// </summary>
    RunOnce,

    /// <summary>
    /// Runs on every boot, in version order among whatever else executes. NOT frozen — the
    /// body always states the CURRENT desired form, the shape DbUp uses for views and macros:
    /// edit it in place, and every boot reasserts it. Each execution is still recorded.
    /// </summary>
    RunAlways,
}
