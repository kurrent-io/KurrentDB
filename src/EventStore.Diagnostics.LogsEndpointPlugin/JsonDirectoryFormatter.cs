// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace EventStore.Diagnostics.LogsEndpointPlugin;

public class JsonDirectoryFormatter : IDirectoryFormatter {
	// Implementation based on: https://github.com/dotnet/aspnetcore/blob/main/src/Middleware/StaticFiles/src/HtmlDirectoryFormatter.cs
	private const string JsonUtf8 = "application/json; charset=utf-8";
	private const string JsonExt = ".json";
	private const int MaxResultCount = 1000;

	public async Task GenerateContentAsync(HttpContext ctx, IEnumerable<IFileInfo> contents) {
		ArgumentNullException.ThrowIfNull(ctx);
		ArgumentNullException.ThrowIfNull(contents);

		ctx.Response.ContentType = JsonUtf8;

		if (HttpMethods.IsHead(ctx.Request.Method)) {
			return;
		}

		var filesToList = contents
			.Where(c => !c.IsDirectory && c.Name.EndsWith(JsonExt))
			.ToList();

		await using var response = ctx.Response.BodyWriter.AsStream();
		await using var writer = new Utf8JsonWriter(response);

		writer.WriteStartArray();

		foreach (var fileInfo in filesToList.OrderByDescending(f => f.LastModified).Take(MaxResultCount)) {
			writer.WriteStartObject();
			writer.WriteString("name", fileInfo.Name);

			if (fileInfo.PhysicalPath != null) {
				var createdAt = File.GetCreationTime(fileInfo.PhysicalPath);
				writer.WriteString("createdAt", createdAt.ToString("O"));
			}

			writer.WriteString("lastModified", fileInfo.LastModified.LocalDateTime.ToString("O"));
			writer.WriteNumber("size", fileInfo.Length);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();

		await writer.FlushAsync();
	}
}
