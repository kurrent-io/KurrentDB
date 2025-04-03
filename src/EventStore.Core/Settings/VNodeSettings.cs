// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Services.Monitoring;

namespace EventStore.Core.Settings;

public class SingleVNodeSettings {
	public readonly IPEndPoint ExternalTcpEndPoint;
	public readonly IPEndPoint ExternalSecureTcpEndPoint;
	public readonly IPEndPoint HttpEndPoint;
	public readonly string[] HttpPrefixes;
	public readonly bool EnableTrustedAuth;
	public readonly X509Certificate2 Certificate;
	public readonly int WorkerThreads;

	public readonly TimeSpan MinFlushDelay;
	public readonly TimeSpan PrepareTimeout;
	public readonly TimeSpan CommitTimeout;

	public readonly bool DisableScavengeMerging;

	public readonly TimeSpan StatsPeriod;
	public readonly StatsStorage StatsStorage;

	public readonly bool SkipInitializeStandardUsersCheck;

	public readonly TimeSpan TcpTimeout;

	public SingleVNodeSettings(IPEndPoint externalTcpEndPoint,
		IPEndPoint externalSecureTcpEndPoint,
		IPEndPoint httpEndPoint,
		string[] httpPrefixes,
		bool enableTrustedAuth,
		X509Certificate2 certificate,
		int workerThreads,
		TimeSpan minFlushDelay,
		TimeSpan prepareTimeout,
		TimeSpan commitTimeout,
		TimeSpan statsPeriod,
		TimeSpan tcpTimeout,
		StatsStorage statsStorage = StatsStorage.StreamAndFile,
		bool skipInitializeStandardUsersCheck = false,
		bool disableScavengeMerging = false) {
		Ensure.NotNull(httpEndPoint, nameof(httpEndPoint));
		Ensure.NotNull(httpPrefixes, "httpPrefixes");
		if (externalSecureTcpEndPoint != null)
			Ensure.NotNull(certificate, "certificate");
		Ensure.Positive(workerThreads, "workerThreads");

		ExternalTcpEndPoint = externalTcpEndPoint;
		ExternalSecureTcpEndPoint = externalSecureTcpEndPoint;
		HttpEndPoint = httpEndPoint;
		HttpPrefixes = httpPrefixes;
		EnableTrustedAuth = enableTrustedAuth;
		Certificate = certificate;
		WorkerThreads = workerThreads;

		MinFlushDelay = minFlushDelay;
		PrepareTimeout = prepareTimeout;
		CommitTimeout = commitTimeout;

		StatsPeriod = statsPeriod;
		StatsStorage = statsStorage;

		SkipInitializeStandardUsersCheck = skipInitializeStandardUsersCheck;
		DisableScavengeMerging = disableScavengeMerging;
		TcpTimeout = tcpTimeout;
	}

	public override string ToString() {
		return string.Format("ExternalTcpEndPoint: {0},\n"
		                     + "ExternalSecureTcpEndPoint: {1},\n"
		                     + "HttpEndPoint: {2},\n"
		                     + "HttpPrefixes: {3},\n"
		                     + "EnableTrustedAuth: {4},\n"
		                     + "Certificate: {5},\n"
		                     + "WorkerThreads: {6}\n"
		                     + "MinFlushDelay: {7}\n"
		                     + "PrepareTimeout: {8}\n"
		                     + "CommitTimeout: {9}\n"
		                     + "StatsPeriod: {10}\n"
		                     + "StatsStorage: {11}",
			ExternalTcpEndPoint == null ? "n/a" : ExternalTcpEndPoint.ToString(),
			ExternalSecureTcpEndPoint == null ? "n/a" : ExternalSecureTcpEndPoint.ToString(),
			HttpEndPoint,
			string.Join(", ", HttpPrefixes),
			EnableTrustedAuth,
			Certificate == null ? "n/a" : Certificate.ToString(verbose: true),
			WorkerThreads,
			MinFlushDelay,
			PrepareTimeout,
			CommitTimeout,
			StatsPeriod,
			StatsStorage);
	}
}
