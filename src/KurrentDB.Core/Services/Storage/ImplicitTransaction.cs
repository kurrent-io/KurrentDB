// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Services.Storage;

public class ImplicitTransaction<TStreamId> {
	private readonly List<IPrepareLogRecord<TStreamId>> _prepares = [];

	public long? Position { get; private set; }

	public IReadOnlyList<IPrepareLogRecord<TStreamId>> Prepares => _prepares;

	public void Process(IPrepareLogRecord<TStreamId> prepare) {
		Position = prepare.TransactionPosition;

		if (!prepare.Flags.HasAnyOf(PrepareFlags.Data | PrepareFlags.StreamDelete))
			return;

		_prepares.Add(prepare);
	}

	public void Clear() {
		Position = null;
		_prepares.Clear();
	}
}
