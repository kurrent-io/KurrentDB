// ReSharper disable CheckNamespace

using KurrentDB.Common.Utils;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.Core;

[PublicAPI]
public static class ClientMessageExtensions {
	public static ReadResponseException MapToException(this ClientMessage.NotHandled notHandled) {
		return notHandled.Reason switch {
			ClientMessage.NotHandled.Types.NotHandledReason.NotReady   => new ReadResponseException.NotHandled.ServerNotReady(),
			ClientMessage.NotHandled.Types.NotHandledReason.TooBusy    => new ReadResponseException.NotHandled.ServerBusy(),
			ClientMessage.NotHandled.Types.NotHandledReason.NotLeader  => LeaderException(),
			ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly => LeaderException()
		};

		ReadResponseException LeaderException() =>
			notHandled.LeaderInfo is not null
				? new ReadResponseException.NotHandled.LeaderInfo(notHandled.LeaderInfo.Http.GetHost(), notHandled.LeaderInfo.Http.GetPort())
				: new ReadResponseException.NotHandled.NoLeaderInfo();
	}
}
