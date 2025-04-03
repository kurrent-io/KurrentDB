// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EventStore.Common.Utils;
using EventStore.Core.Services.Transport.Http;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Util;

public class MiniWeb {
	private readonly string _localWebRootPath;
	private readonly string _fileSystemRoot;
	private static readonly ILogger Logger = Serilog.Log.ForContext<MiniWeb>();

	public MiniWeb(string localWebRootPath) : this(localWebRootPath, GetWebRootFileSystemDirectory()) {
	}

	public MiniWeb(string localWebRootPath, string fileSystemRoot) {
		Logger.Information("Starting MiniWeb for {localWebRootPath} ==> {fileSystemRoot}", localWebRootPath,
			fileSystemRoot);
		_localWebRootPath = localWebRootPath;
		_fileSystemRoot = fileSystemRoot;
	}

	public void RegisterControllerActions(IHttpService service) {
		var pattern = _localWebRootPath + "/{*remaining_path}";
		Logger.Verbose("Binding MiniWeb to {path}", pattern);
		service.RegisterAction(
			new ControllerAction(pattern, HttpMethod.Get, Codec.NoCodecs, new ICodec[] {Codec.ManualEncoding}, new Operation(Operations.Node.StaticContent)),
			OnStaticContent);
	}

	private void OnStaticContent(HttpEntityManager http, UriTemplateMatch match) {
		var contentLocalPath = match.BoundVariables["remaining_path"];
		ReplyWithContent(http, contentLocalPath);
	}

	private void ReplyWithContent(HttpEntityManager http, string contentLocalPath) {
		//NOTE: this is fix for Mono incompatibility in UriTemplate behavior for /a/b{*C}
		if (("/" + contentLocalPath).StartsWith(_localWebRootPath))
			contentLocalPath = contentLocalPath.Substring(_localWebRootPath.Length);

		//_logger.Trace("{contentLocalPath} requested from MiniWeb", contentLocalPath);
		try {
			var extensionToContentType = new Dictionary<string, string> {
				{".png", "image/png"},
				{".svg", "image/svg+xml"},
				{".woff", "application/x-font-woff"},
				{".woff2", "application/x-font-woff"},
				{".ttf", "application/font-sfnt"},
				{".jpg", "image/jpeg"},
				{".jpeg", "image/jpeg"},
				{".css", "text/css"},
				{".htm", "text/html"},
				{".html", "text/html"},
				{".js", "application/javascript"},
				{".json", "application/json"},
				{".ico", "image/vnd.microsoft.icon"}
			};

			var extension = Path.GetExtension(contentLocalPath);
			var fullPath = Path.Combine(_fileSystemRoot, contentLocalPath);

			string contentType;
			if (string.IsNullOrEmpty(extension)
			    || !extensionToContentType.TryGetValue(extension.ToLower(), out contentType)
			    || !File.Exists(fullPath)) {
				Logger.Information("Replying 404 for {contentLocalPath} ==> {fullPath}", contentLocalPath, fullPath);
				http.ReplyTextContent(
					"Not Found", 404, "Not Found", ContentType.PlainText, null,
					ex => Logger.Information(ex, "Error while replying from MiniWeb"));
			} else {
				var config = GetWebPageConfig(contentType);
				var content = File.ReadAllBytes(fullPath);

				http.Reply(content,
					config.Code,
					config.Description,
					config.ContentType,
					config.Encoding,
					config.Headers,
					ex => Logger.Information(ex, "Error while replying from MiniWeb"));
			}
		} catch (Exception ex) {
			http.ReplyTextContent(ex.ToString(), 500, "Internal Server Error", ContentType.PlainText, null,
				Console.WriteLine);
		}
	}

	private static ResponseConfiguration GetWebPageConfig(string contentType) {
		var encoding = contentType.StartsWith("image") ? null : Helper.UTF8NoBom;
		int? cacheSeconds =
#if RELEASE || CACHE_WEB_CONTENT
                60*60; // 1 hour
#else
			null; // no caching
#endif
		// ReSharper disable ExpressionIsAlwaysNull
		return Configure.Ok(contentType, encoding, null, cacheSeconds, isCachePublic: true);
		// ReSharper restore ExpressionIsAlwaysNull
	}

	public static string GetWebRootFileSystemDirectory(string debugPath = null) {
		string fileSystemWebRoot;
		try {
			if (!string.IsNullOrEmpty(debugPath)) {
				var sf = new StackFrame(0, true);
				var fileName = sf.GetFileName();
				var sourceWebRootDirectory = string.IsNullOrEmpty(fileName)
					? ""
					: Path.GetFullPath(Path.Combine(fileName, @"..\..\..", debugPath));
				fileSystemWebRoot = Directory.Exists(sourceWebRootDirectory)
					? sourceWebRootDirectory
					: Locations.WebContentDirectory;
			} else {
				fileSystemWebRoot = Locations.WebContentDirectory;
			}
		} catch (Exception) {
			fileSystemWebRoot = Locations.WebContentDirectory;
		}

		return fileSystemWebRoot;
	}
}
