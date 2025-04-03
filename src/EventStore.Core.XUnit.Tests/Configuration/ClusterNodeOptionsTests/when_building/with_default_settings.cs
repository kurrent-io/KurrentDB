// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Options;
using EventStore.Common.Utils;
using EventStore.Core.Authentication.DelegatedAuthentication;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.Chunks;
using NUnit.Framework;

namespace EventStore.Core.XUnit.Tests.Configuration.ClusterNodeOptionsTests.when_building;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_default_node_as_single_node<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options;
	}

	public with_default_node_as_single_node() : base(disableMemoryOptimization: true) {
	}

	[Test]
	public void should_create_single_cluster_node() {
		Assert.IsNotNull(_node);
		Assert.AreEqual(1, _options.Cluster.ClusterSize, "ClusterNodeCount");
		Assert.IsInstanceOf<DelegatedAuthenticationProvider>(_node.AuthenticationProvider);
		Assert.AreEqual(StatsStorage.File, _options.Database.StatsStorage);
	}

	[Test]
	public void should_have_default_endpoints() {
		Assert.AreEqual(new IPEndPoint(IPAddress.Loopback, 1112), _node.NodeInfo.InternalSecureTcp);
		Assert.AreEqual(new IPEndPoint(IPAddress.Loopback, 2113), _node.NodeInfo.HttpEndPoint);
	}

	[Test]
	public void should_set_command_line_args_to_default_values() {
		Assert.AreEqual(false, _options.Interface.EnableTrustedAuth, "EnableTrustedAuth");
		Assert.AreEqual(false, _options.Application.LogHttpRequests, "LogHttpRequests");
		Assert.AreEqual(0, _options.Application.WorkerThreads, "WorkerThreads");
		Assert.AreEqual(true, _options.Cluster.DiscoverViaDns, "DiscoverViaDns");
		Assert.AreEqual(30, _options.Application.StatsPeriodSec, "StatsPeriod");
		Assert.AreEqual(false, _options.Application.DisableHttpCaching, "DisableHTTPCaching");
		Assert.AreEqual(false, _options.Database.SkipDbVerify, "VerifyDbHash");
		Assert.AreEqual(TFConsts.MinFlushDelayMs.TotalMilliseconds, _options.Database.MinFlushDelayMs, "MinFlushDelay");
		Assert.AreEqual(30, _options.Database.ScavengeHistoryMaxAge,
			"ScavengeHistoryMaxAge");
		Assert.AreEqual(false, _options.Database.DisableScavengeMerging,
			"DisableScavengeMerging");
		Assert.AreEqual(false, _options.Interface.DisableAdminUi, "AdminOnPublic");
		Assert.AreEqual(false, _options.Interface.DisableStatsOnHttp, "StatsOnPublic");
		Assert.AreEqual(false, _options.Interface.DisableGossipOnHttp, "GossipOnPublic");
		Assert.AreEqual(1_000_000, _options.Database.MaxMemTableSize, "MaxMemtableEntryCount");
		Assert.AreEqual(false, _options.Projection.RunProjections > ProjectionType.System,
			"StartStandardProjections");
		Assert.AreEqual(false, _options.Database.UnsafeIgnoreHardDelete,
			"UnsafeIgnoreHardDeletes");
		Assert.That(string.IsNullOrEmpty(_options.Database.Index), "IndexPath");
		Assert.AreEqual(2000, _options.Database.PrepareTimeoutMs, "PrepareTimeout");
		Assert.AreEqual(2000, _options.Database.CommitTimeoutMs, "CommitTimeout");
		Assert.AreEqual(2000, _options.Database.WriteTimeoutMs, "WriteTimeout");

		Assert.AreEqual(700, _options.Interface.ReplicationHeartbeatInterval, "ReplicationHeartbeatInterval");

		Assert.AreEqual(700, _options.Interface.ReplicationHeartbeatTimeout,
			"ReplicationHeartbeatTimeout");

		Assert.AreEqual(TFConsts.ChunkSize, _node.Db.Config.ChunkSize, "ChunkSize");
		Assert.AreEqual(TFConsts.ChunksCacheSize, _node.Db.Config.MaxChunksCacheSize, "MaxChunksCacheSize");
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_default_node_as_node_in_a_cluster<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	[Test]
	public void should_create_single_cluster_node() {
		Assert.IsNotNull(_node);
		Assert.AreEqual(_clusterSize, _options.Cluster.ClusterSize, "ClusterNodeCount");
		Assert.IsInstanceOf<DelegatedAuthenticationProvider>(_node.AuthenticationProvider);
		Assert.AreEqual(StatsStorage.File, _options.Database.StatsStorage);
	}

	[Test]
	public void should_have_default_secure_endpoints() {
		var internalTcp = new IPEndPoint(IPAddress.Loopback, 1112);
		var externalTcp = new IPEndPoint(IPAddress.Loopback, 1113);
		var httpEndPoint = new IPEndPoint(IPAddress.Loopback, 2113);

		Assert.AreEqual(internalTcp, _node.NodeInfo.InternalSecureTcp);
		Assert.AreEqual(httpEndPoint, _node.NodeInfo.HttpEndPoint);

		Assert.AreEqual(internalTcp.ToDnsEndPoint(), _node.GossipAdvertiseInfo.InternalSecureTcp);
		Assert.AreEqual(httpEndPoint.ToDnsEndPoint(), _node.GossipAdvertiseInfo.HttpEndPoint);
	}

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options;
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_default_node_as_node_in_an_insecure_cluster<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	[Test]
	public void should_create_single_cluster_node() {
		Assert.IsNotNull(_node);
		Assert.AreEqual(_clusterSize, _options.Cluster.ClusterSize, "ClusterNodeCount");
		Assert.IsInstanceOf<DelegatedAuthenticationProvider>(_node.AuthenticationProvider);
		Assert.AreEqual(StatsStorage.File, _options.Database.StatsStorage);
	}

	[Test]
	public void should_have_default_endpoints() {
		var internalTcp = new IPEndPoint(IPAddress.Loopback, 1112);
		var externalTcp = new IPEndPoint(IPAddress.Loopback, 1113);
		var httpEndPoint = new IPEndPoint(IPAddress.Loopback, 2113);

		Assert.AreEqual(internalTcp, _node.NodeInfo.InternalTcp);
		Assert.AreEqual(httpEndPoint, _node.NodeInfo.HttpEndPoint);

		Assert.AreEqual(internalTcp.ToDnsEndPoint(), _node.GossipAdvertiseInfo.InternalTcp);
		Assert.AreEqual(httpEndPoint.ToDnsEndPoint(), _node.GossipAdvertiseInfo.HttpEndPoint);
	}

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options.Insecure();
	}
}
