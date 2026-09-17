// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using EventStore.Plugins.Licensing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KurrentDB.Licensing.Tests;

// LicenseMonitor itself is covered by LicenseMonitorTests; these cover the only decision this
// class makes, which is that a single node deployment does not need the entitlement.
public sealed class MultiNodeClusterLicenseMonitorTests {
	private static readonly TimeSpan RejectionTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan NoRejectionDelay = TimeSpan.FromSeconds(1);

	// Returns the exception the license was rejected with, or null if it was not rejected within the timeout.
	private static async Task<Exception?> MonitorAsync(
		bool isSingleNode, FakeLicenseService licenseService, TimeSpan timeout) {

		var sut = new MultiNodeClusterLicenseMonitor(isSingleNode);

		sut.Monitor(licenseService, NullLoggerFactory.Instance);

		var completed = await Task.WhenAny(licenseService.Rejected, Task.Delay(timeout));
		return completed == licenseService.Rejected ? await licenseService.Rejected : null;
	}

	[Fact]
	public async Task given_cluster_when_license_without_entitlement_then_rejects() {
		Assert.NotNull(await MonitorAsync(
			isSingleNode: false,
			new FakeLicenseService("SOME_OTHER_ENTITLEMENT"),
			RejectionTimeout));
	}

	[Fact]
	public async Task given_single_node_when_license_without_entitlement_then_does_not_reject() {
		Assert.Null(await MonitorAsync(
			isSingleNode: true,
			new FakeLicenseService("SOME_OTHER_ENTITLEMENT"),
			NoRejectionDelay));
	}

	private class FakeLicenseService : ILicenseService {
		private readonly TaskCompletionSource<Exception> _rejected = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public FakeLicenseService(params string[] entitlements) {
			var claims = new Dictionary<string, object>();
			foreach (var entitlement in entitlements)
				claims[entitlement] = "true";

			var license = License.Create(claims);

			SelfLicense = license;
			CurrentLicense = license;
			Licenses = new BehaviorSubject<License>(license);
		}

		public Task<Exception> Rejected => _rejected.Task;

		public License SelfLicense { get; }

		public License? CurrentLicense { get; }

		public IObservable<License> Licenses { get; }

		public void RejectLicense(Exception ex) => _rejected.TrySetResult(ex);
	}
}
