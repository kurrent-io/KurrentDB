// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KurrentDB.Components.Tools;

public partial class JsonViewer : ComponentBase {
	[Inject]
	public IJSRuntime JsRuntime { get; set; } = null!;

	IJSObjectReference _libraryReference;

	string Id { get; } = $"TJV{Random.Shared.Next()}";

	protected override async Task OnAfterRenderAsync(bool firstRender) {
		if (!firstRender)
			return;

		_libraryReference = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "https://unpkg.com/@alenaksu/json-viewer@2.0.0/dist/json-viewer.bundle.js");
	}

	public ValueTask Render(string json) => JsRuntime.InvokeVoidAsync("eval", $"document.querySelector('#{Id}').data = {json};");
}
