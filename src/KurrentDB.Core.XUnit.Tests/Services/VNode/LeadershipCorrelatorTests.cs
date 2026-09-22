// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Services.VNode;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Services.VNode;

public class LeadershipCorrelatorTests {
	private readonly LeadershipCorrelator _sut = new();

	private static MemberInfoLite Leader() => new() {
		InstanceId = Guid.NewGuid(),
		HttpEndPoint = new DnsEndPoint("leader", 2113),
		ReplicationEndPoint = new DnsEndPoint("leader", 1112),
		ClientHttpEndPoint = new DnsEndPoint("leader", 2113),
		EpochNumber = 0,
	};

	[Fact]
	public void starts_with_unknown_leader() {
		Assert.True(_sut.LeaderIsUnknown);
		Assert.False(_sut.TryGetLeaderInfo(out _));
	}

	[Fact]
	public void correlates_after_set_leader() {
		var leader = Leader();
		_sut.SetLeader(leader, out var correlationId);

		Assert.False(_sut.LeaderIsUnknown);
		Assert.True(_sut.IsCorrelatedWith(correlationId));
		Assert.True(_sut.TryGetLeaderInfo(out var info));
		Assert.Same(leader, info);
	}

	[Fact]
	public void does_not_correlate_after_reset() {
		_sut.SetLeader(Leader(), out var correlationId);

		_sut.ResetLeader();

		Assert.True(_sut.LeaderIsUnknown);
		Assert.False(_sut.IsCorrelatedWith(correlationId));
		Assert.False(_sut.TryGetLeaderInfo(out _));
	}

	[Fact]
	public void does_not_correlate_with_previous_leader_after_new_leader() {
		_sut.SetLeader(Leader(), out var firstCorrelationId);

		_sut.SetLeader(Leader(), out var secondCorrelationId);

		Assert.False(_sut.IsCorrelatedWith(firstCorrelationId));
		Assert.True(_sut.IsCorrelatedWith(secondCorrelationId));
	}

	[Fact]
	public void does_not_correlate_with_arbitrary_guid() {
		_sut.SetLeader(Leader(), out _);

		Assert.False(_sut.IsCorrelatedWith(Guid.NewGuid()));
	}

	[Fact]
	public void returns_leader_info_when_correlated() {
		var leader = Leader();
		_sut.SetLeader(leader, out var correlationId);

		Assert.True(_sut.IsCorrelatedWith(correlationId, out var info));
		Assert.Same(leader, info);
	}

	[Fact]
	public void does_not_return_leader_info_when_not_correlated() {
		_sut.SetLeader(Leader(), out var correlationId);
		_sut.ResetLeader();

		Assert.False(_sut.IsCorrelatedWith(correlationId, out var info));
		Assert.Null(info);
	}
}
