// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace EventStore.Diagnostics.LogsEndpointPlugin.Tests;

public class JsonDirectoryFormatterTests {
	[Fact]
	public async Task formats_content_correctly() {
		var dirContent = new List<IFileInfo>() {
			TestFile.Named("file-1.json"),
			TestFile.Named("file-2.json")
		};

		var json = await GenerateResponse(dirContent);

		Assert.NotNull(json);
		Assert.Equal(JsonValueKind.Array, json.GetValueKind());
		Assert.Equal(dirContent.Count, json.AsArray().Count);
	}

	[Fact]
	public async Task skips_directories() {
		List<IFileInfo> files = [
			TestFile.Named("file-1.json"),
			TestFile.Named("file-2.json")
		];
		List<IFileInfo> directories = [
			TestDirectory.Named("uploads")
		];

		var dirContent = new List<IFileInfo>(files).Concat(directories);

		var json = await GenerateResponse(dirContent);

		Assert.NotNull(json);
		Assert.Equal(JsonValueKind.Array, json.GetValueKind());
		Assert.Equal(files.Count, json.AsArray().Count);
	}

	[Fact]
	public async Task only_returns_json_files() {
		List<IFileInfo> jsonFiles = [
			TestFile.Named("file-1.json"),
			TestFile.Named("file-2.json")
		];
		List<IFileInfo> otherFiles = [
			TestFile.Named("upload.exe"),
			TestFile.Named("upload.jpg"),
		];

		var dirContent = new List<IFileInfo>(jsonFiles).Concat(otherFiles);

		var json = await GenerateResponse(dirContent);

		Assert.NotNull(json);
		Assert.Equal(JsonValueKind.Array, json.GetValueKind());
		Assert.Equal(jsonFiles.Count, json.AsArray().Count);
	}

	[Fact]
	public async Task responds_with_most_recent_modification_first() {
		var now = DateTimeOffset.Now;
		List<IFileInfo> dirContent = [
			TestFile.Named("monday.json").ModifiedAt(now.AddDays(-4)),
			TestFile.Named("tuesday.json").ModifiedAt(now.AddDays(-3)),
			TestFile.Named("wednesday.json").ModifiedAt(now.AddDays(-2)),
			TestFile.Named("thursday.json").ModifiedAt(now.AddDays(-1)),
			TestFile.Named("friday.json").ModifiedAt(now),
		];

		var json = await GenerateResponse(dirContent);

		Assert.NotNull(json);
		Assert.Equal(JsonValueKind.Array, json.GetValueKind());

		var filenames = json.AsArray().Select(f => f?["name"]?.GetValue<string>());

		Assert.Equal([
			"friday.json",
			"thursday.json",
			"wednesday.json",
			"tuesday.json",
			"monday.json"
		], filenames);
	}

	[Fact]
	public async Task returns_a_maximum_of_1000_items() {
		var dirContent = Enumerable
			.Range(1, 2113)
			.Select(i => TestFile
				.Named($"log-file-{i}.json")
				.ModifiedAt(DateTimeOffset.Now.AddDays(i)));

		var json = await GenerateResponse(dirContent);

		Assert.NotNull(json);
		Assert.Equal(JsonValueKind.Array, json.GetValueKind());
		Assert.Equal(1000, json.AsArray().Count);

		Assert.Equal(
			"log-file-2113.json",
			json.AsArray().First()!["name"]!.GetValue<string>());
	}

	[Fact]
    public async Task returns_a_date_as_local_time_with_time_zone_information() {
	    var now = DateTimeOffset.Now;
	    List<IFileInfo> dirContent = [
		    TestFile.Named("file-1.json").ModifiedAt(now),
	    ];

    	var json = await GenerateResponse(dirContent);

    	Assert.NotNull(json);
    	Assert.Equal(JsonValueKind.Array, json.GetValueKind());
    	Assert.Equal(dirContent.Count, json.AsArray().Count);

    	Assert.Equal(
    		now.ToString("o"),
    		json.AsArray().First()!["lastModified"]!.GetValue<string>());
    }

	[Fact]
	public async Task returns_an_empty_array_when_there_is_nothing_to_return() {
		var json = await GenerateResponse(Array.Empty<IFileInfo>());

		Assert.NotNull(json);
		Assert.Equal(JsonValueKind.Array, json.GetValueKind());
		Assert.Empty(json.AsArray());
	}

	private static async Task<JsonNode?> GenerateResponse(IEnumerable<IFileInfo> dirContent) {
		var formatter = new JsonDirectoryFormatter();
		var ctx = new DefaultHttpContext();
		var stream = new MemoryStream();

		ctx.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(stream));

		await formatter.GenerateContentAsync(ctx, dirContent);

		stream.Position = 0;

		return await JsonNode.ParseAsync(stream);
	}

	private static class TestDirectory {
		public static TestFile Named(string name) {
			return new TestFile(
				Name: name,
				Exists: true,
				IsDirectory: true,
				Length: 303,
				LastModified: DateTimeOffset.Now,
				PhysicalPath: null);
		}
	}

	private record TestFile(bool Exists, bool IsDirectory, DateTimeOffset LastModified, long Length, string Name,
		string? PhysicalPath) : IFileInfo {

		public Stream CreateReadStream() {
			throw new NotImplementedException();
		}

		public static TestFile Named(string name) {
			return new TestFile(
				Name: name,
				Exists: true,
				IsDirectory: false,
				Length: 303,
				LastModified: DateTimeOffset.Now,
				PhysicalPath: null);
		}

		public TestFile ModifiedAt(DateTimeOffset offset) {
			return this with {
				LastModified = offset
			};
		}
	}
}
