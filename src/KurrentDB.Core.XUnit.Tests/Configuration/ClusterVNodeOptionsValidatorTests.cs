// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
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

	[Theory]
	[InlineData(-1, false)]
	[InlineData(0, true)]
	[InlineData(1024, true)]
	public void sql_engine_temp_directory_size_limit_cannot_be_negative(long sizeLimit, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Database = new() {
				SqlEngineTempDirectorySizeLimit = sizeLimit,
			},
		};

		void When() => ClusterVNodeOptionsValidator.Validate(options);

		if (expectedValid) {
			When();
		} else {
			Assert.Throws<ArgumentOutOfRangeException>(When);
		}
	}

	[Fact]
	public void sql_engine_temp_directory_cannot_start_with_a_tilde() {
		var options = new ClusterVNodeOptions {
			Database = new() {
				SqlEngineTempDirectory = "~/spill",
			},
		};

		Assert.Throws<ApplicationInitializationException>(() => {
			ClusterVNodeOptionsValidator.Validate(options);
		});
	}

	[Theory]
	// distinct directories
	[InlineData("/a/db", "/a/index", "/a/spill", true)]
	// the sql engine temp directory can be shared with neither the db nor the index
	[InlineData("/a/db", "/a/index", "/a/db", false)]
	[InlineData("/a/db", "/a/index", "/a/index", false)]
	// paths are normalized before they are compared
	[InlineData("/a/db", "/a/index", "/a/db/", false)]
	[InlineData("/a/db", "/a/index", "/a/./db", false)]
	// the db and the index still cannot be shared with each other
	[InlineData("/a/db", "/a/db", "/a/spill", false)]
	// unset directories do not collide with each other
	[InlineData("/a/db", null, "", true)]
	public void directories_cannot_point_to_the_same_directory(
		string db, string index, string sqlEngineTempDirectory, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Database = new() {
				Db = db,
				Index = index,
				SqlEngineTempDirectory = sqlEngineTempDirectory,
			},
		};

		void When() => ClusterVNodeOptionsValidator.Validate(options);

		if (expectedValid) {
			When();
		} else {
			Assert.Throws<ApplicationInitializationException>(When);
		}
	}

	[Theory]
	// A read-only replica takes no part in electing a leader, so it cannot be a Kontrol Plane node
	[InlineData(true, true, true, false)]
	// but it can be a Data Plane node, which is how a replica joins a Kontrol Plane cluster
	[InlineData(true, false, true, true)]
	// and a regular node is either both or neither
	[InlineData(false, true, true, true)]
	[InlineData(false, false, false, true)]
	public void read_only_replica_cannot_be_a_kontrol_plane_node(
		bool readOnlyReplica, bool isKontrolPlaneNode, bool isDataPlaneNode, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Cluster = new() {
				ClusterSize = 3,
				ReadOnlyReplica = readOnlyReplica,
			},
			KontrolPlane = new() {
				IsKontrolPlaneNode = isKontrolPlaneNode,
				IsDataPlaneNode = isDataPlaneNode,
				KontrolPlaneBootstrapSeed = Seed,
				KontrolPlaneApiSeed = Seed,
			},
		};

		Validate(options, expectedValid);
	}

	[Theory]
	// Both planes, or neither
	[InlineData(true, true, false, true)]
	[InlineData(false, false, false, true)]
	// One without the other is not supported yet
	[InlineData(true, false, false, false)]
	[InlineData(false, true, false, false)]
	// except on a read-only replica, which can only ever be a Data Plane node
	[InlineData(false, true, true, true)]
	public void a_node_participates_in_both_planes_or_neither(
		bool isKontrolPlaneNode, bool isDataPlaneNode, bool readOnlyReplica, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Cluster = new() {
				ClusterSize = 3,
				ReadOnlyReplica = readOnlyReplica,
			},
			KontrolPlane = new() {
				IsKontrolPlaneNode = isKontrolPlaneNode,
				IsDataPlaneNode = isDataPlaneNode,
				KontrolPlaneBootstrapSeed = Seed,
				KontrolPlaneApiSeed = Seed,
			},
		};

		Validate(options, expectedValid);
	}

	[Theory]
	// Without a seed the Kontrol Plane nodes cannot find each other
	[InlineData(3, false, false)]
	[InlineData(3, true, true)]
	// A single node is its own Kontrol Plane, so it needs no seed
	[InlineData(1, false, true)]
	[InlineData(1, true, true)]
	public void kontrol_plane_node_in_a_cluster_requires_a_bootstrap_seed(
		int clusterSize, bool hasBootstrapSeed, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Cluster = new() {
				ClusterSize = clusterSize,
			},
			KontrolPlane = new() {
				IsKontrolPlaneNode = true,
				IsDataPlaneNode = true,
				KontrolPlaneBootstrapSeed = hasBootstrapSeed ? Seed : [],
			},
		};

		Validate(options, expectedValid);
	}

	[Theory]
	// A replica runs no Kontroller, so it has nothing to bootstrap against but the seed
	[InlineData(false, false)]
	[InlineData(true, true)]
	public void read_only_replica_requires_an_api_seed(bool hasApiSeed, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Cluster = new() {
				ClusterSize = 3,
				ReadOnlyReplica = true,
			},
			KontrolPlane = new() {
				IsDataPlaneNode = true,
				KontrolPlaneApiSeed = hasApiSeed ? Seed : [],
			},
		};

		Validate(options, expectedValid);
	}

	[Theory]
	[InlineData(5000, 6000, true)]
	[InlineData(6000, 6000, false)]
	[InlineData(7000, 6000, false)]
	public void election_timeout_lower_bound_must_be_below_the_upper_bound(
		int lowerMs, int upperMs, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			KontrolPlane = new() {
				KontrolPlaneLowerElectionTimeoutMs = lowerMs,
				KontrolPlaneUpperElectionTimeoutMs = upperMs,
			},
		};

		Validate(options, expectedValid);
	}

	[Theory]
	[InlineData(0, 6000, false)]
	[InlineData(-1, 6000, false)]
	[InlineData(5000, 0, false)]
	[InlineData(5000, 6000, true)]
	public void timeouts_must_be_positive(int lowerMs, int appointmentMs, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			KontrolPlane = new() {
				KontrolPlaneLowerElectionTimeoutMs = lowerMs,
				KontrolPlaneUpperElectionTimeoutMs = 10_000,
				KontrolPlaneAppointmentTimeoutMs = appointmentMs,
			},
		};

		Validate(options, expectedValid);
	}

	[Theory]
	// The Kontrol Plane keeps a durable record of a node's epoch and identity, so a node taking part in
	// either plane cannot discard its database on restart
	[InlineData(true, true, true, false)]
	// A read-only replica takes part too: it announces itself and is recorded like any other node
	[InlineData(true, false, true, false)]
	// A node on legacy elections is in no such record anywhere, so it may
	[InlineData(true, false, false, true)]
	// And a persistent database is fine in either plane
	[InlineData(false, true, true, true)]
	public void cluster_using_kontrol_plane_requires_a_persistent_database(
		bool memDb, bool isKontrolPlaneNode, bool isDataPlaneNode, bool expectedValid) {
		var options = new ClusterVNodeOptions {
			Cluster = new() {
				ClusterSize = 3,
				// The only node that can be a Data Plane node without being a Kontrol Plane one
				ReadOnlyReplica = isDataPlaneNode && !isKontrolPlaneNode,
			},
			Database = new() {
				MemDb = memDb,
			},
			KontrolPlane = new() {
				IsKontrolPlaneNode = isKontrolPlaneNode,
				IsDataPlaneNode = isDataPlaneNode,
				KontrolPlaneBootstrapSeed = Seed,
				KontrolPlaneApiSeed = Seed,
			},
		};

		Validate(options, expectedValid);
	}

	private static readonly EndPoint[] Seed = [new IPEndPoint(IPAddress.Loopback, 3111)];

	private static void Validate(ClusterVNodeOptions options, bool expectedValid) {
		void When() => ClusterVNodeOptionsValidator.Validate(options);

		if (expectedValid) {
			When();
		} else {
			Assert.Throws<InvalidConfigurationException>(When);
		}
	}
}
