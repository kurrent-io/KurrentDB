// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using EventStore.Core.XUnit.Tests;
using Xunit;

namespace EventStore.PluginHosting.Tests;

public class PluginLoaderTests : IAsyncLifetime {
	private readonly DirectoryFixture<PluginLoaderTests> _fixture = new();

	public async Task InitializeAsync() {
		await _fixture.InitializeAsync();
	}

	public async Task DisposeAsync() {
		for (int i = 0; i < 3; i++) {
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
		await _fixture.DisposeAsync();
	}

	[Fact]
	public void root_plugin_folder_does_not_exist() {
		var rootPluginDirectory = new DirectoryInfo(Path.Combine(_fixture.Directory, "nonexistent"));
		Assert.False(rootPluginDirectory.Exists);

		using var sut = new PluginLoader(rootPluginDirectory);
		Assert.Empty(sut.Load<ICloneable>());
	}

	[Fact]
	public void root_plugin_folder_does_exist_but_is_empty() {
		var rootPluginDirectory = new DirectoryInfo(_fixture.Directory);
		Assert.True(rootPluginDirectory.Exists);

		using var sut = new PluginLoader(rootPluginDirectory);
		Assert.Empty(sut.Load<ICloneable>());
	}

	[Fact]
	public void sub_plugin_folder_does_exist_but_is_empty() {
		var rootPluginDirectory = new DirectoryInfo(_fixture.Directory);
		var subPluginDirectory = rootPluginDirectory.CreateSubdirectory(Guid.NewGuid().ToString());
		Assert.True(subPluginDirectory.Exists);

		using var sut = new PluginLoader(rootPluginDirectory);
		Assert.Empty(sut.Load<ICloneable>());
	}

	[Fact]
	public async Task plugin_exists_in_root_plugin_folder() {
		var rootPluginDirectory = new DirectoryInfo(_fixture.Directory);
		await BuildFakePlugin(rootPluginDirectory);

		using var sut = new PluginLoader(rootPluginDirectory);
		Assert.Single(sut.Load<ICloneable>());
	}

	[Fact]
	public async Task plugin_exists_in_sub_plugin_folder() {
		var rootPluginDirectory = new DirectoryInfo(_fixture.Directory);
		var subPluginDirectory = rootPluginDirectory.CreateSubdirectory(Guid.NewGuid().ToString());
		await BuildFakePlugin(subPluginDirectory);

		using var sut = new PluginLoader(rootPluginDirectory);
		Assert.Single(sut.Load<ICloneable>());
	}

	[Fact]
	public async Task plugin_exists_in_sub_sub_plugin_folder_should_not_get_loaded() {
		var rootPluginDirectory = new DirectoryInfo(_fixture.Directory);
		var subPluginDirectory = rootPluginDirectory.CreateSubdirectory(Guid.NewGuid().ToString());
		var subSubPluginDirectory = subPluginDirectory.CreateSubdirectory(Guid.NewGuid().ToString());
		await BuildFakePlugin(subSubPluginDirectory);

		using var sut = new PluginLoader(rootPluginDirectory);
		Assert.Empty(sut.Load<ICloneable>());
	}

	private static async Task BuildFakePlugin(DirectoryInfo outputFolder) {
		var tcs = new TaskCompletionSource<bool>();
		using var process = new Process {
			StartInfo = new ProcessStartInfo {
				FileName = "dotnet",
				Arguments = $"publish --configuration {BuildConfiguration} --framework=net8.0 --output {outputFolder.FullName}",
				WorkingDirectory = PluginSourceDirectory,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			},
			EnableRaisingEvents = true
		};

		process.Exited += (_, e) => tcs.SetResult(default);
		process.Start();

		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();

		await Task.WhenAll(tcs.Task, stdout, stderr);

		if (process.ExitCode != 0) {
			throw new Exception(stdout.Result);
		}
	}

	private static string PluginSourceDirectory => Path.Combine(Environment.CurrentDirectory, "FakePlugin");

	private const string BuildConfiguration =
#if DEBUG
		"Debug";
#else
		"Release";
#endif
}
