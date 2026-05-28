// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Exceptions;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Configuration;

// Some other tests are in ClusterNodeOptionsTests/when_building
public class ClusterVNodeOptionsValidatorTests {
	[Theory]
	[InlineData(false, false, true)]
	[InlineData(false, true, true)]
	[InlineData(true, false, false)]
	[InlineData(true, true, true)]
	public void archiver_requires_read_only_replica(bool archiver, bool readOnlyReplica, bool expectedValid) {
		// given
		var options = new ClusterVNodeOptions {
			Cluster = new() {
				Archiver = archiver,
				ClusterSize = 3,
				ReadOnlyReplica = readOnlyReplica,
			}
		};

		// when
		void When() {
			ClusterVNodeOptionsValidator.Validate(options);
		}

		// then
		if (expectedValid) {
			When();
		} else {
			Assert.Throws<InvalidConfigurationException>(When);
		}
	}

	[Fact]
	public void archiver_not_compatible_with_unsafe_ignore_hard_delete() {
		// because the archive is not scavenged at the moment and so the tombstones will not be removed
		var options = new ClusterVNodeOptions {
			Cluster = new() {
				Archiver = true,
				ClusterSize = 3,
				ReadOnlyReplica = true,
			},
			Database = new() {
				UnsafeIgnoreHardDelete = true,
			}
		};

		Assert.Throws<InvalidConfigurationException>(() => {
			ClusterVNodeOptionsValidator.Validate(options);
		});
	}

	[Theory]
	// TLS on — no secret needed regardless of cluster size
	[InlineData(false, false, 3, "",       true)]
	// Insecure mode — auth fully disabled, secret is moot
	[InlineData(false, true,  3, "",       true)]
	[InlineData(true,  true,  3, "",       true)]
	// disable-tls (any cluster size) with empty / whitespace secret — invalid
	[InlineData(true,  false, 1, "",       false)]
	[InlineData(true,  false, 1, "   ",    false)]
	[InlineData(true,  false, 3, "",       false)]
	[InlineData(true,  false, 3, "   ",    false)]
	// disable-tls (any cluster size) with a real secret — valid
	[InlineData(true,  false, 1, "secret", true)]
	[InlineData(true,  false, 3, "secret", true)]
	// A secret set where it has no effect is allowed (validator only warns, doesn't throw)
	[InlineData(false, false, 3, "secret", true)]  // TLS on
	[InlineData(true,  true,  3, "secret", true)]  // insecure
	public void disable_tls_requires_cluster_secret(
		bool disableTls, bool insecure, int clusterSize, string clusterSecret, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Application = new() {
				DisableTls = disableTls,
				Insecure = insecure,
			},
			Cluster = new() {
				ClusterSize = clusterSize,
				ClusterSecret = clusterSecret,
			},
		};

		void When() => ClusterVNodeOptionsValidator.Validate(options);

		if (expectedValid) {
			When();
		} else {
			Assert.Throws<InvalidConfigurationException>(When);
		}
	}
}
