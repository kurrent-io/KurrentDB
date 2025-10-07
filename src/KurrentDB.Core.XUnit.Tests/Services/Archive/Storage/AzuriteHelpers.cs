// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Azure.Storage.Blobs;
using KurrentDB.Core.Services.Archive;

namespace KurrentDB.Core.XUnit.Tests.Services.Archive.Storage;

internal static class AzuriteHelpers {
	private const string AzureContainerName = "kurrentdb";

	private const string AzureConnectionString =
		"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

	public static void ConfigureEnvironment() {
		var client = new BlobServiceClient(AzureConnectionString).GetBlobContainerClient(AzureContainerName);

		if (client.Exists()) {
			client.Delete();
		}

		client.Create();
	}

	public static AzureOptions Options { get; } = new() {
		ConnectionString = AzureConnectionString,
		Container = AzureContainerName,
		Authentication = AzureOptions.AuthenticationType.ConnectionString,
	};
}
