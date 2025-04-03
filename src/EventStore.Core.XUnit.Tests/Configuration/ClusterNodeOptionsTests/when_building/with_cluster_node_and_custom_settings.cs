// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Certificates;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace EventStore.Core.XUnit.Tests.Configuration.ClusterNodeOptionsTests.when_building;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_cluster_dns_name<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options with {
			Cluster = options.Cluster with {
				ClusterDns = "ClusterDns"
			}
		};

	[Test]
	public void should_set_discover_via_dns_to_true() {
		Assert.IsTrue(_options.Cluster.DiscoverViaDns);
	}

	[Test]
	public void should_set_cluster_dns_name() {
		Assert.AreEqual("ClusterDns", _options.Cluster.ClusterDns);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_dns_discovery_disabled_and_no_gossip_seeds<TLogFormat, TStreamId> {
	private Exception _caughtException;
	protected ClusterVNodeOptions _options;

	[OneTimeSetUp]
	public void TestFixtureSetUp() {
		_options = new ClusterVNodeOptions {
				Cluster = new() {
					DiscoverViaDns = false
				}
			}
			.ReduceMemoryUsageForTests()
			.RunInMemory();
		try {
			_ = new ClusterVNode<TStreamId>(_options, LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory,
				certificateProvider: new OptionsCertificateProvider());
		} catch (Exception e) {
			_caughtException = e;
		}
	}

	[Test]
	public void should_throw_an_exception() {
		Assert.IsNotNull(_caughtException);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_dns_discovery_disabled_and_gossip_seeds_defined<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	private EndPoint[] _gossipSeeds = {
		new DnsEndPoint("127.0.1.10", 1111),
		new DnsEndPoint("127.0.1.10", 1112),
	};

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options.WithGossipSeeds(_gossipSeeds);

	[Test]
	public void should_set_discover_via_dns_to_false() {
		Assert.IsFalse(_options.Cluster.DiscoverViaDns);
	}

	[Test]
	public void should_set_the_gossip_seeds() {
		CollectionAssert.AreEqual(_gossipSeeds, _options.Cluster.GossipSeed);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_custom_gossip_seeds<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	private readonly DnsEndPoint[] _gossipSeeds = { new("127.0.1.15", 2112), new("127.0.1.15", 3112) };

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options.WithGossipSeeds(_gossipSeeds);

	[Test]
	public void should_turn_off_discovery_by_dns() {
		Assert.IsFalse(_options.Cluster.DiscoverViaDns);
	}

	[Test]
	public void should_set_the_gossip_seeds() {
		CollectionAssert.AreEqual(_gossipSeeds, _options.Cluster.GossipSeed);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_custom_external_ip_address_as_advertise_info<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options
			.Insecure()
			.WithExternalTcpOn(new IPEndPoint(IPAddress.Loopback, 11130))
			.WithReplicationEndpointOn(new IPEndPoint(IPAddress.Loopback, 11120))
			.AdvertiseExternalHostAs(new DnsEndPoint("196.168.1.1", 11131))
			.AdvertiseNodeAs(new DnsEndPoint("196.168.1.1", 21130));
	}

	[Test]
	public void should_set_the_custom_advertise_info_for_external() {
		Assert.AreEqual(new DnsEndPoint("196.168.1.1", 21130),
			_node.GossipAdvertiseInfo.HttpEndPoint);
	}

	[Test]
	public void should_set_the_loopback_address_as_advertise_info_for_internal() {
		Assert.AreEqual(new DnsEndPoint(IPAddress.Loopback.ToString(), 11120), _node.GossipAdvertiseInfo.InternalTcp);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_0_0_0_0_as_external_ip_address_and_custom_advertise_info<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options
			.Insecure()
			.WithReplicationEndpointOn(new IPEndPoint(IPAddress.Any, 11120))
			.WithExternalTcpOn(new IPEndPoint(IPAddress.Any, 11130))
			.AdvertiseExternalHostAs(new DnsEndPoint("10.0.0.1", 11131));
	}

	[Test]
	public void should_set_the_custom_advertise_info_for_external() {
		Assert.AreEqual(new DnsEndPoint("10.0.0.1", 2113),
			_node.GossipAdvertiseInfo.HttpEndPoint);
	}

	[Test]
	public void should_set_the_non_loopback_address_as_advertise_info_for_internal() {
		Assert.AreEqual(new DnsEndPoint(IPFinder.GetNonLoopbackAddress().ToString(), 11120),
			_node.GossipAdvertiseInfo.InternalTcp);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_0_0_0_0_as_external_ip_address_with_no_explicit_advertise_info_set<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options
			.Insecure()
			.WithNodeEndpointOn(new IPEndPoint(IPAddress.Any, 21130))
			.WithExternalTcpOn(new IPEndPoint(IPAddress.Any, 11130))
			.WithReplicationEndpointOn(new IPEndPoint(IPAddress.Loopback, 11120));
	}

	[Test]
	public void should_use_the_non_default_loopback_ip_as_advertise_info_for_external() {
		Assert.AreEqual(new DnsEndPoint(IPFinder.GetNonLoopbackAddress().ToString(), 21130),
			_node.GossipAdvertiseInfo.HttpEndPoint);
	}

	[Test]
	public void should_use_loopback_ip_as_advertise_info_for_internal() {
		Assert.AreEqual(new DnsEndPoint(IPAddress.Loopback.ToString(), 11120), _node.GossipAdvertiseInfo.InternalTcp);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	with_0_0_0_0_for_internal_and_external_ips_with_advertise_info_set_for_external<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) {
		return options
			.Insecure()
			.WithNodeEndpointOn(new IPEndPoint(IPAddress.Loopback, 21130))
			.WithExternalTcpOn(new IPEndPoint(IPAddress.Loopback, 11130))
			.WithReplicationEndpointOn(new IPEndPoint(IPAddress.Any, 11120))
			.AdvertiseExternalHostAs(new DnsEndPoint("10.0.0.1", 11131))
			.AdvertiseNodeAs(new DnsEndPoint("10.0.0.1", 21131));
	}

	[Test]
	public void should_set_the_custom_advertise_info_for_external() {
		Assert.AreEqual(new DnsEndPoint("10.0.0.1", 21131),
			_node.GossipAdvertiseInfo.HttpEndPoint);
	}

	[Test]
	public void should_use_the_non_default_loopback_ip_as_advertise_info_for_internal() {
		Assert.AreEqual(new DnsEndPoint(IPFinder.GetNonLoopbackAddress().ToString(), 11120),
			_node.GossipAdvertiseInfo.InternalTcp);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_cluster_custom_password_for_admin_and_ops_user<TLogFormat, TStreamId> : ClusterMemberScenario<TLogFormat, TStreamId> {
	private const string _adminPassword = "Admin";
	private const string _opsPassword = "Ops";

	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options with {
			DefaultUser = new ClusterVNodeOptions.DefaultUserOptions { DefaultAdminPassword = _adminPassword, DefaultOpsPassword = _opsPassword }
		};

	[Test]
	public void should_set_the_custom_admin_and_ops_user_password() {
		Assert.AreEqual(_adminPassword, _options.DefaultUser.DefaultAdminPassword);
		Assert.AreEqual(_opsPassword, _options.DefaultUser.DefaultOpsPassword);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class with_cluster_custom_settings_check_for_environment_only_options<TLogFormat, TStreamId> : SingleNodeScenario<TLogFormat,
		TStreamId> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options with {
		};

	private IConfigurationRoot _configurationRoot;

	[Test]
	public void should_return_error_when_default_password_options_pass_through_command_line() {
		var args = new[] {
			"--DefaultAdminPassword=Admin2023#",
			"--DefaultOpsPassword=Ops2023#"
		};

		_configurationRoot = new ConfigurationBuilder()
			.AddKurrentDefaultValues(new Dictionary<string, object> {
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultAdminPassword)] = SystemUsers.DefaultAdminPassword,
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultOpsPassword)] = SystemUsers.DefaultOpsPassword
			})
			.AddKurrentCommandLine(args)
			.Build();

		var clusterVNodeOptions = ClusterVNodeOptions.FromConfiguration(_configurationRoot);

		Assert.NotNull(clusterVNodeOptions.CheckForEnvironmentOnlyOptions());
	}

	[Test]
	public void should_return_null_when_default_password_options_pass_through_environment_variables() {
		var prefix = KurrentConfigurationKeys.Prefix.ToUpper();
		var args = Array.Empty<string>();
		IDictionary environmentVariables = new Dictionary<string, string>();
		environmentVariables.Add($"{prefix}_DEFAULT_ADMIN_PASSWORD", "Admin#");
		environmentVariables.Add($"{prefix}_DEFAULT_OPS_PASSWORD", "Ops#");

		_configurationRoot = new ConfigurationBuilder()
			.AddKurrentDefaultValues(new Dictionary<string, object> {
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultAdminPassword)] = SystemUsers.DefaultAdminPassword,
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultOpsPassword)] = SystemUsers.DefaultOpsPassword
			})
			.AddKurrentCommandLine(args)
			.AddKurrentEnvironmentVariables(environmentVariables)
			.Build();

		var clusterVNodeOptions = ClusterVNodeOptions.FromConfiguration(_configurationRoot);

		Assert.Null(clusterVNodeOptions.CheckForEnvironmentOnlyOptions());
	}
}
