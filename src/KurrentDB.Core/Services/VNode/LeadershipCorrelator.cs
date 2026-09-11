// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.VNode;

public readonly struct CorrelatedLeaderInfo {
	public MemberInfoLite LeaderInfo { get; }
	public Guid CorrelationId { get; }

	public CorrelatedLeaderInfo(MemberInfoLite leaderInfo, Guid correlationId) {
		LeaderInfo = Ensure.NotNull(leaderInfo, nameof(leaderInfo));
		CorrelationId = Ensure.NotEmptyGuid(correlationId, nameof(correlationId));
	}
}

public class LeadershipCorrelator {
	private CorrelatedLeaderInfo? _correlatedLeaderInfo;

	public bool LeaderIsUnknown => _correlatedLeaderInfo is null;

	public void SetLeader(MemberInfoLite leaderInfo, out Guid correlationId) {
		correlationId = Guid.NewGuid();
		_correlatedLeaderInfo = new(leaderInfo, correlationId);
	}

	public void ResetLeader() {
		_correlatedLeaderInfo = null;
	}

	public bool TryGetLeaderInfo(out MemberInfoLite leaderInfo) {
		leaderInfo = _correlatedLeaderInfo?.LeaderInfo;
		return leaderInfo is not null;
	}

	public bool TryGetLeaderInfo(out MemberInfoLite leaderInfo, out Guid correlationId) {
		leaderInfo = _correlatedLeaderInfo?.LeaderInfo;
		correlationId = _correlatedLeaderInfo?.CorrelationId ?? default;
		return leaderInfo is not null;
	}

	public bool IsCorrelatedWith(
		Guid correlationId,
		out MemberInfoLite leaderInfo,
		out Guid outCorrelationId) {

		outCorrelationId = default;
		leaderInfo = default;

		if (_correlatedLeaderInfo is not { } cli ||
			cli.CorrelationId != correlationId)
			return false;

		leaderInfo = cli.LeaderInfo;
		outCorrelationId = cli.CorrelationId;
		return true;
	}

	public bool IsCorrelatedWith(Guid correlationId, out MemberInfoLite leaderInfo) =>
		IsCorrelatedWith(correlationId, out leaderInfo, out _);

	public bool IsCorrelatedWith(Guid correlationId) =>
		IsCorrelatedWith(correlationId, out _, out _);

	public bool IsCorrelatedWith(
		SystemMessage.StateChangeMessage message,
		out MemberInfoLite leaderInfo,
		out Guid correlationId) =>
		IsCorrelatedWith(message.CorrelationId, out leaderInfo, out correlationId);

	public bool IsCorrelatedWith(SystemMessage.StateChangeMessage message, out MemberInfoLite leaderInfo) =>
		IsCorrelatedWith(message.CorrelationId, out leaderInfo, out _);

	public bool IsCorrelatedWith(SystemMessage.StateChangeMessage message) =>
		IsCorrelatedWith(message.CorrelationId, out _, out _);

}
