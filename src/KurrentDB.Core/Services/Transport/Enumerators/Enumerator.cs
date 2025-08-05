// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Channels;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.Transport.Enumerators;

file static class ChannelOptionsFactory {
	public static BoundedChannelOptions CreateCatchUpChannelOptions(int bufferSize) =>
		new(bufferSize) {
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = true
		};

	public static BoundedChannelOptions CreateLiveChannelOptions(int bufferSize) =>
		new(bufferSize) {
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = true
		};
}

public static partial class Enumerator {
	private const int DefaultLiveBufferSize = 32;
	public const int DefaultCatchUpBufferSize = 32;
	public const int DefaultReadBatchSize = 32; // TODO  JPB make this configurable

	private static readonly BoundedChannelOptions DefaultCatchUpChannelOptions
		= ChannelOptionsFactory.CreateCatchUpChannelOptions(DefaultCatchUpBufferSize);

	private static readonly BoundedChannelOptions DefaultLiveChannelOptions
		= ChannelOptionsFactory.CreateLiveChannelOptions(DefaultLiveBufferSize);

	private static Channel<ReadResponse> CreateCatchUpChannel(int size) {
		var options = size == DefaultCatchUpBufferSize
			? DefaultCatchUpChannelOptions
			: ChannelOptionsFactory.CreateCatchUpChannelOptions(size);
		return Channel.CreateBounded<ReadResponse>(options);
	}

	private static Channel<T> CreateLiveChannel<T>(int size) {
		var options = size == DefaultLiveBufferSize
			? DefaultLiveChannelOptions
			: ChannelOptionsFactory.CreateLiveChannelOptions(size);
		return Channel.CreateBounded<T>(options);
	}

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
