// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace KurrentDB.Core.Services.Archive.Storage.Azure;

internal static class AzureOptionsExtensions {
	public static BlobContainerClient CreateClient(this AzureOptions options)
		=> options.CreateServiceClient().GetBlobContainerClient(options.Container);

	private static BlobServiceClient CreateServiceClient(this AzureOptions options) {
		TokenCredential credential;
		switch (options.Authentication) {
			case AzureOptions.AuthenticationType.ConnectionString:
				return new(options.ConnectionStringOrServiceUrl);
			case AzureOptions.AuthenticationType.SystemAssignedIdentity:
				credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
				break;
			case AzureOptions.AuthenticationType.UserAssignedClientId:
				credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.UserIdentity));
				break;
			default:
				credential = new DefaultAzureCredential();
				break;
		}

		return new(new Uri(options.ConnectionStringOrServiceUrl, UriKind.Absolute), credential);
	}
}
