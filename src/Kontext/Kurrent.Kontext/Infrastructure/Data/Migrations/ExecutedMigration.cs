// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Infrastructure.Data.Migrations;

/// <summary>
/// One journal entry: which key ran and how long it took. The journal supplies the clock.
/// </summary>
public readonly record struct ExecutedMigration(uint Version, string Key, string Script, TimeSpan Duration);
