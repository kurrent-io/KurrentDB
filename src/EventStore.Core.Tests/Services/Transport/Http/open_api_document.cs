// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Integration;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class open_api_document<TLogFormat, TStreamId> : specification_with_a_single_node<TLogFormat, TStreamId> {

	[Test]
	public async Task should_document_the_actions_of_all_controllers() {
		var skipActionPaths = new [] {
			"/admin/login",
			"/users/$current",
			// handled by: /stats/{statPath}
			"/stats/replication",
			"/stats/tcp",
			// redirected
			"/users/",
			"/streams/$all/",
			"/streams/%24all/",
			"/streams/{stream}/",
			"/streams/{stream}/metadata/",
		};

		var httpActions = _node.Node.HttpService.Actions
			.Where(a => !skipActionPaths.Contains(a.UriTemplate))
			.Where(a => !a.UriTemplate.StartsWith("/test"))
			// exclude AtomPub controller actions
			.Where(a => !a.UriTemplate.Contains("embed={embed}"))
			.ToArray();

		var absoluteSwaggerPath = HelperExtensions.GetFilePathFromAssembly("swagger.yaml");

		var openApiReader = new Microsoft.OpenApi.Readers.OpenApiTextReaderReader();
		var openApiDoc = File.OpenText(absoluteSwaggerPath);
		var result = await openApiReader.ReadAsync(openApiDoc);

		Assert.IsEmpty(result.OpenApiDiagnostic.Errors, "OpenAPI document has errors!");
		Assert.IsEmpty(result.OpenApiDiagnostic.Warnings, "OpenAPI document has warnings!");

		var missingPaths = httpActions
			.Select(ActionUriPath)
			.Except(result.OpenApiDocument.Paths.Keys);

		Assert.IsEmpty(missingPaths, "OpenAPI document has extra or missing paths!");

		string ActionUriPath(ControllerAction a) {
			var path = a.UriTemplate;
			if (a.UriTemplate.Contains("?")) {
				var parts = path.Split("?");
				path = parts.First();
			}
			return path.Replace("{*", "{");
		}
	}
}
