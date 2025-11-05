// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System;
using System.IO;
using KurrentDB.Common.Exceptions;
using KurrentDB.Core.Services;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.Util;
using Serilog;

namespace KurrentDB.Core;

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

		if (options.Grpc.KeepAliveInterval is >= 0 and < 10) {
			Log.Warning("Specified KeepAliveInterval of {KeepAliveInterval} is less than recommended 10_000 ms.", options.Grpc.KeepAliveInterval);
		}

		if (options.Application.MaxAppendSize > TFConsts.ChunkSize) {
			throw new ArgumentOutOfRangeException(nameof(options.Application.MaxAppendSize),
				$"{nameof(options.Application.MaxAppendSize)} exceeded {TFConsts.ChunkSize} bytes.");
		}

		if (options.Application.MaxAppendEventSize > TFConsts.MaxLogRecordSize) {
			throw new ArgumentOutOfRangeException(nameof(options.Application.MaxAppendEventSize),
				$"{nameof(options.Application.MaxAppendEventSize)} exceeded {TFConsts.MaxLogRecordSize} bytes.");
		}

		if (options.Application.MaxAppendEventSize > options.Application.MaxAppendSize) {
			throw new ArgumentOutOfRangeException(nameof(options.Application.MaxAppendEventSize),
				$"{nameof(options.Application.MaxAppendEventSize)} must be less than or equal to {nameof(options.Application.MaxAppendSize)}.");
		}

		if (options.Cluster.DiscoverViaDns && string.IsNullOrWhiteSpace(options.Cluster.ClusterDns))
			throw new ArgumentException(
				"Either DNS Discovery must be disabled (and seeds specified), or a cluster DNS name must be provided.");

		if (options.Database.Db.StartsWith("~")) {
			throw new ApplicationInitializationException(
				"The given database path starts with a '~'. KurrentDB does not expand '~'.");
		}

		if (options.Database is { Index: not null, Db: not null }) {
			var absolutePathIndex = Path.GetFullPath(options.Database.Index);
			var absolutePathDb = Path.GetFullPath(options.Database.Db);
			if (absolutePathDb.Equals(absolutePathIndex)) {
				throw new ApplicationInitializationException(
					$"The given database ({absolutePathDb}) and index ({absolutePathIndex}) paths cannot point to the same directory.");
			}
		}

		if (options.Cluster.GossipSeed.Length > 1 && options.Cluster.ClusterSize == 1) {
			throw new ApplicationInitializationException(
				"The given ClusterSize is set to 1 but GossipSeeds are multiple. We will never be able to sync up with this configuration.");
		}

		if (options.Cluster is { ReadOnlyReplica: true, ClusterSize: <= 1 }) {
			throw new InvalidConfigurationException(
				"This node cannot be configured as a Read Only Replica as these node types are only supported in a clustered configuration.");
		}

		if (options.Cluster is { Archiver: true, ReadOnlyReplica: false }) {
			throw new InvalidConfigurationException(
				"The Archiver node must also be a Read Only Replica.");
		}

		if (options.Cluster.Archiver && options.Database.UnsafeIgnoreHardDelete) {
			throw new InvalidConfigurationException(
				"The Archiving feature is not compatible with UnsafeIgnoreHardDelete.");
		}

		ValidateConcurrency(options.Database);
	}

	public static bool ValidateForStartup(ClusterVNodeOptions options) {
		if (options.Cluster is { DiscoverViaDns: false, GossipSeed.Length: 0, ClusterSize: 1 }) {
			Log.Information(
				"DNS discovery is disabled, but no gossip seed endpoints have been specified. Since "
				+ "the cluster size is set to 1, this may be intentional. Gossip seeds can be specified "
				+ "using the `GossipSeed` option.");
		}

		var environmentOnlyOptions = options.CheckForEnvironmentOnlyOptions();
		if (environmentOnlyOptions != null) {
			Log.Error("Invalid Option {EnvironmentOnlyOptions}", environmentOnlyOptions);
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

	private static void ValidateConcurrency(ClusterVNodeOptions.DatabaseOptions options) {
		switch (options.ReaderThreadsCount, options.MaxConcurrentReadersCount) {
			case (ReaderThreadsCount: > 0, MaxConcurrentReadersCount: > 0)
				when options.MaxConcurrentReadersCount != options.ReaderThreadsCount:
				throw new InvalidConfigurationException(
					"ReaderThreadsCount and MaxConcurrentReadersCount cannot be set to different positive values.");
			case (ReaderThreadsCount: > 0, MaxConcurrentReadersCount: 0):
				options.MaxConcurrentReadersCount = options.ReaderThreadsCount;
				break;
		}
	}
}
