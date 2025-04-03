// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Licensing;
using Microsoft.AspNetCore.Components;

namespace KurrentDB.Components.Licensed;

public abstract class WithLicense : ComponentBase {
	[Inject] public ILicenseService LicenseService { get; set; }


	protected override void OnInitialized() {
		HasLicense = LicenseService.CurrentLicense != null;
		base.OnInitialized();
	}

	protected bool HasLicense;
}
