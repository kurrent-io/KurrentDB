// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Channels;
using EventStore.Common.Utils;
using EventStore.Core.Messages;

namespace EventStore.Core.Services.Transport.Enumerators;

public static partial class Enumerator {
	private const int MaxLiveEventBufferCount = 32;
	private const int ReadBatchSize = 32; // TODO  JPB make this configurable

	private static readonly BoundedChannelOptions BoundedChannelOptions =
		new(MaxLiveEventBufferCount) {
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = true
		};

	private static readonly BoundedChannelOptions LiveChannelOptions =
		new(MaxLiveEventBufferCount) {
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = true
		};

	private static bool TryHandleNotHandled(ClientMessage.NotHandled notHandled, out ReadResponseException exception) {
		exception = null;
		switch (notHandled.Reason) {
			case ClientMessage.NotHandled.Types.NotHandledReason.NotReady:
				exception = new ReadResponseException.NotHandled.ServerNotReady();
				return true;
			case ClientMessage.NotHandled.Types.NotHandledReason.TooBusy:
				exception = new ReadResponseException.NotHandled.ServerBusy();
				return true;
			case ClientMessage.NotHandled.Types.NotHandledReason.NotLeader:
			case ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly:
				switch (notHandled.LeaderInfo) {
					case { } leaderInfo:
						exception = new ReadResponseException.NotHandled.LeaderInfo(leaderInfo.Http.GetHost(), leaderInfo.Http.GetPort());
						return true;
					default:
						exception = new ReadResponseException.NotHandled.NoLeaderInfo();
						return true;
				}

			default:
				return false;
		}
	}
}
