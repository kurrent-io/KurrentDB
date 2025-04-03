// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
using System;
using System.IO;
using EventStore.Common.Exceptions;
using EventStore.Core.Services;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.Util;
using Serilog;

namespace EventStore.Core;

public static class ClusterVNodeOptionsValidator {
	public static void Validate(ClusterVNodeOptions options) {
		if (options == null) {
			throw new ArgumentNullException(nameof(options));
		}

		if (options.Interface.NodeIp == null) {
			throw new ArgumentNullException(nameof(options.Interface.NodeIp));
		}

		if (options.Interface.ReplicationIp == null) {
			throw new ArgumentNullException(nameof(options.Interface.ReplicationIp));
		}

		if (options.Cluster.ClusterSize <= 0) {
			throw new ArgumentOutOfRangeException(nameof(options.Cluster.ClusterSize), options.Cluster.ClusterSize,
				$"{nameof(options.Cluster.ClusterSize)} must be greater than 0.");
		}

		if (options.Cluster.ClusterDns == null) {
			throw new ArgumentNullException(nameof(options.Cluster.ClusterDns));
		}

		if (options.Cluster.GossipSeed == null) {
			throw new ArgumentNullException(nameof(options.Cluster.GossipSeed));
		}

		if (options.Database.InitializationThreads <= 0) {
			throw new ArgumentOutOfRangeException(nameof(options.Database.InitializationThreads),
				options.Database.InitializationThreads,
				$"{nameof(options.Database.InitializationThreads)} must be greater than 0.");
		}

		if (options.Grpc.KeepAliveTimeout < 0) {
			throw new ArgumentOutOfRangeException(
				$"Invalid {nameof(options.Grpc.KeepAliveTimeout)} {options.Grpc.KeepAliveTimeout}. Please provide a positive integer.");
		}

		if (options.Grpc.KeepAliveInterval < 0) {
			throw new ArgumentOutOfRangeException(
				$"Invalid {nameof(options.Grpc.KeepAliveInterval)} {options.Grpc.KeepAliveInterval}. Please provide a positive integer.");
		}

		if (options.Grpc.KeepAliveInterval >= 0 && options.Grpc.KeepAliveInterval < 10) {
			Log.Warning(
				$"Specified {nameof(options.Grpc.KeepAliveInterval)} of {options.Grpc.KeepAliveInterval} is less than recommended 10_000 ms.");
		}

		if (options.Application.MaxAppendSize > TFConsts.EffectiveMaxLogRecordSize) {
			throw new ArgumentOutOfRangeException(nameof(options.Application.MaxAppendSize),
				$"{nameof(options.Application.MaxAppendSize)} exceeded {TFConsts.EffectiveMaxLogRecordSize} bytes.");
		}

		if (options.Cluster.DiscoverViaDns && string.IsNullOrWhiteSpace(options.Cluster.ClusterDns))
			throw new ArgumentException(
				"Either DNS Discovery must be disabled (and seeds specified), or a cluster DNS name must be provided.");

		if (options.Database.Db.StartsWith("~")) {
			throw new ApplicationInitializationException(
				"The given database path starts with a '~'. KurrentDB does not expand '~'.");
		}

		if (options.Database.Index != null && options.Database.Db != null) {
			string absolutePathIndex = Path.GetFullPath(options.Database.Index);
			string absolutePathDb = Path.GetFullPath(options.Database.Db);
			if (absolutePathDb.Equals(absolutePathIndex)) {
				throw new ApplicationInitializationException(
					$"The given database ({absolutePathDb}) and index ({absolutePathIndex}) paths cannot point to the same directory.");
			}
		}

		if (options.Cluster.GossipSeed.Length > 1 && options.Cluster.ClusterSize == 1) {
			throw new ApplicationInitializationException(
				"The given ClusterSize is set to 1 but GossipSeeds are multiple. We will never be able to sync up with this configuration.");
		}

		if (options.Cluster.ReadOnlyReplica && options.Cluster.ClusterSize <= 1) {
			throw new InvalidConfigurationException(
				"This node cannot be configured as a Read Only Replica as these node types are only supported in a clustered configuration.");
		}

		if (options.Cluster.Archiver && !options.Cluster.ReadOnlyReplica) {
			throw new InvalidConfigurationException(
				"The Archiver node must also be a Read Only Replica.");
		}

		if (options.Cluster.Archiver && options.Database.UnsafeIgnoreHardDelete) {
			throw new InvalidConfigurationException(
				"The Archiving feature is not compatible with UnsafeIgnoreHardDelete.");
		}
	}

	public static bool ValidateForStartup(ClusterVNodeOptions options) {
		if (!options.Cluster.DiscoverViaDns && options.Cluster.GossipSeed.Length == 0 &&
		    options.Cluster.ClusterSize == 1) {
			Log.Information(
				"DNS discovery is disabled, but no gossip seed endpoints have been specified. Since "
				+ "the cluster size is set to 1, this may be intentional. Gossip seeds can be specified "
				+ "using the `GossipSeed` option.");
		}

		var environmentOnlyOptions = options.CheckForEnvironmentOnlyOptions();
		if (environmentOnlyOptions != null) {
			Log.Error($"Invalid Option {environmentOnlyOptions}");
			return false;
		}

		if (options.Application.Insecure || options.Auth.AuthenticationType != Opts.AuthenticationTypeDefault) {
			if (options.DefaultUser.DefaultAdminPassword != SystemUsers.DefaultAdminPassword) {
				Log.Error("Cannot set default admin password when not using the internal authentication.");
				return false;
			}

			if (options.DefaultUser.DefaultOpsPassword != SystemUsers.DefaultOpsPassword) {
				Log.Error("Cannot set default ops password when not using the internal authentication.");
				return false;
			}
		}

		return true;
	}
}
