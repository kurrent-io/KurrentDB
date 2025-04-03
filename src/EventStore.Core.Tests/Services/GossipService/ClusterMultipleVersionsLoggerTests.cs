// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Cluster;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Gossip;
using FluentAssertions;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.GossipService;

public abstract class ClusterMultipleVersionsLoggerTests {
	
	public class if_gossip_received_has_alive_nodes_on_old_version : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			new GossipMessage.GossipReceived(new CallbackEnvelope(AssertGossipReply),
				new ClusterInfo(
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.DefaultVersion),
					// nodeThree is dead
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
						isAlive: false),
					MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.OldVersion)), _nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
					isAlive: false),
				MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1, esVersion: VersionInfo.OldVersion));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeFour.HttpEndPoint, VersionInfo.OldVersion },
				{ _nodeTwo.HttpEndPoint, VersionInfo.DefaultVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void should_detect_version_mismatch() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			
			Assert.AreEqual(2, numDistinctKnownVersions);
		}

		private void AssertGossipReply(Message message) {
			message.Should()
				.BeEquivalentTo(new GossipMessage.SendGossip(GetExpectedClusterInfo(), _currentNode.HttpEndPoint));
		}
	}

	public class if_gossip_received_has_alive_nodes_on_different_version : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			new GossipMessage.GossipReceived(new NoopEnvelope(),
				new ClusterInfo(
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.DefaultVersion),
					// nodeThree is dead
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
						isAlive: false),
					MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.4")),
				_nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
					isAlive: false),
				MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.4"));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeFour.HttpEndPoint, "1.1.1.4" },
				{ _nodeTwo.HttpEndPoint, VersionInfo.DefaultVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void should_detect_version_mismatch() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			Assert.AreEqual(2, numDistinctKnownVersions);
		}
	}

	public class if_gossip_received_has_alive_nodes_on_same_version : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			new GossipMessage.GossipReceived(new NoopEnvelope(),
				new ClusterInfo(
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.DefaultVersion),
					// nodeThree is dead
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
						isAlive: false),
					MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.DefaultVersion)), _nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
					isAlive: false),
				MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion));
		}

		[Test]
		public void should_not_detect_version_mismatch() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
				out int numDistinctKnownVersions);
			Assert.AreEqual(1, numDistinctKnownVersions);
		}
	}

	public class if_gossip_received_from_old_node_with_NO_previous_version_info : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			// nodeTwo is sending gossip; this is an old node hence, no version info (<null> value) for any node
			new GossipMessage.GossipReceived(new NoopEnvelope(),
				new ClusterInfo(MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: null),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: null,
						isAlive: true)), _nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: VersionInfo.OldVersion),
				// since currentNode has no previous information about nodeThree, its version info is Unknown as long as currentNode is concerned
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.UnknownVersion));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeThree.HttpEndPoint, VersionInfo.UnknownVersion },
				{ _nodeTwo.HttpEndPoint, VersionInfo.OldVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void version_should_be_unknown() {
			// version of nodeThree is Unknown
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			Assert.AreEqual(2, numDistinctKnownVersions);
		}
	}

	public class if_gossip_received_from_old_node_with_previous_version_info : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources(
			//after this gossip from nodeThree, currentNode has information about nodeThree
			new GossipMessage.GossipReceived(new NoopEnvelope(),
				new ClusterInfo(MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: "1.1.1.3",
					isAlive: true)), _nodeThree.HttpEndPoint));

		protected override Message When() =>
			// nodeTwo is sending gossip; this is an old node hence, no version info (<null> value) for any node
			new GossipMessage.GossipReceived(new NoopEnvelope(),
				new ClusterInfo(MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 2, esVersion: null),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 2, esVersion: null,
						isAlive: true)), _nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 2, esVersion: VersionInfo.OldVersion),
				// version info from previous gossip (from nodeThree) will be retained
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 2, esVersion: "1.1.1.3"));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeThree.HttpEndPoint, "1.1.1.3" },
				{ _nodeTwo.HttpEndPoint, VersionInfo.OldVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void should_retain_previous_version_info() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			Assert.AreEqual(3, numDistinctKnownVersions);
		}
	}

	public class if_Get_gossip_received_has_alive_nodes_on_old_version : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			new GossipMessage.GetGossipReceived(new ClusterInfo(
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.DefaultVersion),
					// nodeThree is dead	
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
						isAlive: false),
					MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.OldVersion)),
				_nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
					isAlive: false),
				MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1, esVersion: VersionInfo.OldVersion));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeFour.HttpEndPoint, VersionInfo.OldVersion },
				{ _nodeTwo.HttpEndPoint, VersionInfo.DefaultVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void should_detect_version_mismatch() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			Assert.AreEqual(2, numDistinctKnownVersions);
		}
	}

	public class if_Get_gossip_received_has_alive_nodes_on_different_version : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			new GossipMessage.GetGossipReceived(new ClusterInfo(
					MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
						esVersion: VersionInfo.DefaultVersion),
					// nodeThree is dead	
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
						isAlive: false),
					MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.4")),
				_nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
					isAlive: false),
				MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.4"));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeFour.HttpEndPoint, "1.1.1.4" },
				{ _nodeTwo.HttpEndPoint, VersionInfo.DefaultVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void should_detect_version_mismatch() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			Assert.AreEqual(2, numDistinctKnownVersions);
		}
	}

	public class if_Get_gossip_received_has_alive_nodes_on_same_versions : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			new GossipMessage.GetGossipReceived(new ClusterInfo(
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion),
				// nodeThree is dead	
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
					isAlive: false),
				MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion)), _nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: "1.1.1.3",
					isAlive: false),
				MemberInfoForVNode(_nodeFour, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.DefaultVersion));
		}

		[Test]
		public void should_not_detect_version_mismatch() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
				out int numDistinctKnownVersions);
			Assert.AreEqual(1, numDistinctKnownVersions);
		}
	}

	public class if_GET_gossip_received_from_old_node_with_NO_previous_version_info : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources();

		protected override Message When() =>
			// nodeTwo is sending gossip; this is an old node hence, no version info (<null> value) for any node
			new GossipMessage.GetGossipReceived(
				new ClusterInfo(MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: null),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1, esVersion: null,
						isAlive: true)), _nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 1, esVersion: VersionInfo.OldVersion),
				// since currentNode has no previous information about nodeThree, its version info is Unknown as far as currentNode is concerned
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: VersionInfo.UnknownVersion));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeThree.HttpEndPoint, VersionInfo.UnknownVersion },
				{ _nodeTwo.HttpEndPoint, VersionInfo.OldVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void version_should_be_unknown() {
			// version of nodeThree is Unknown
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			Assert.AreEqual(2, numDistinctKnownVersions);
		}
	}

	public class if_GET_gossip_received_from_old_node_with_previous_version_info : NodeGossipServiceTestFixture {
		protected override Message[] Given() => GivenSystemInitializedWithKnownGossipSeedSources(
			//after this gossip from nodeThree, currentNode has information about nodeThree
			new GossipMessage.GossipReceived(new NoopEnvelope(),
				new ClusterInfo(MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 1,
					esVersion: "1.1.1.3",
					isAlive: true)), _nodeThree.HttpEndPoint));

		protected override Message When() =>
			// nodeTwo is sending gossip; this is an old node hence, no version info (<null> value) for any node
			new GossipMessage.GetGossipReceived(
				new ClusterInfo(MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 2, esVersion: null),
					MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 2, esVersion: null,
						isAlive: true)), _nodeTwo.HttpEndPoint);

		private ClusterInfo GetExpectedClusterInfo() {
			return new ClusterInfo(
				MemberInfoForVNode(_currentNode, _timeProvider.UtcNow, esVersion: VersionInfo.DefaultVersion),
				MemberInfoForVNode(_nodeTwo, _timeProvider.UtcNow, epochNumber: 2, esVersion: VersionInfo.OldVersion),
				// version info from previous gossip (from nodeThree) will be retained
				MemberInfoForVNode(_nodeThree, _timeProvider.UtcNow, epochNumber: 2, esVersion: "1.1.1.3"));
		}

		private Dictionary<EndPoint, string> GetExpectedEndPointVsVersion() {
			Dictionary<EndPoint, string> endpointVsVersion = new Dictionary<EndPoint, string> {
				{ _nodeThree.HttpEndPoint, "1.1.1.3" },
				{ _nodeTwo.HttpEndPoint, VersionInfo.OldVersion },
				{ _currentNode.HttpEndPoint, VersionInfo.DefaultVersion }
			};
			return endpointVsVersion;
		}

		[Test]
		public void should_retain_previous_version_info() {
			ExpectMessages(new GossipMessage.GossipUpdated(GetExpectedClusterInfo()));

			Dictionary<EndPoint, string> ipAddressVsVersion =
				ClusterMultipleVersionsLogger.GetIPAddressVsVersion(GetExpectedClusterInfo(),
					out int numDistinctKnownVersions);
			ipAddressVsVersion.Should().BeEquivalentTo(GetExpectedEndPointVsVersion());
			Assert.AreEqual(3, numDistinctKnownVersions);
		}
	}
}
