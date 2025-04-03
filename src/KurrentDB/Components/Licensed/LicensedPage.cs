// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace KurrentDB.Components.Licensed;

public abstract class LicensedPage : LicensedPageCore {
	private protected sealed override RenderFragment Body => BuildRenderTree;

	// Allow content to be provided by a .razor file but without
	// overriding the content of the base class
	protected new virtual void BuildRenderTree(RenderTreeBuilder builder)
	{
	}
}
