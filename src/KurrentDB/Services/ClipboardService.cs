// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace KurrentDB.Services;

public class ClipboardService(IJSRuntime jsRuntime) {
	public ValueTask<string> ReadTextAsync() => jsRuntime.InvokeAsync<string>("navigator.clipboard.readText");

	public ValueTask WriteTextAsync(string text) => jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
}
