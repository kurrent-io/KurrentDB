// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Transport.Enumerators;
using static KurrentDB.Core.Messages.ClientMessage.NotHandled.Types;
using static KurrentDB.Core.Services.Transport.Enumerators.ReadResponseException.NotHandled;

namespace KurrentDB.Core;

[PublicAPI]
public static class ClientMessageExtensions {
	public static ReadResponseException MapToException(this ClientMessage.NotHandled notHandled) {
		return notHandled.Reason switch {
			NotHandledReason.NotReady   => new ServerNotReady(),
			NotHandledReason.TooBusy    => new ServerBusy(),
			NotHandledReason.NotLeader  => LeaderException(),
			NotHandledReason.IsReadOnly => LeaderException(),
			_ => throw new ArgumentOutOfRangeException(nameof(notHandled.Reason), notHandled.Reason, null)
		};

		ReadResponseException LeaderException() =>
			notHandled.LeaderInfo is not null
				? new ReadResponseException.NotHandled.LeaderInfo(notHandled.LeaderInfo.Http.GetHost(), notHandled.LeaderInfo.Http.GetPort())
				: new NoLeaderInfo();
	}
}
