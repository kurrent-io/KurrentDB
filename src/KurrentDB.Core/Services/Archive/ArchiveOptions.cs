// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Exceptions;

namespace KurrentDB.Core.Services.Archive;

public class ArchiveOptions {
	public bool Enabled { get; init; } = false;
	public StorageType StorageType { get; init; } = StorageType.Unspecified;
	public FileSystemOptions FileSystem { get; init; } = new();
	public S3Options S3 { get; init; } = new();
	public AzureOptions Azure { get; init; } = new();
	public GcpOptions GCP { get; init; } = new();
	public RetentionOptions RetainAtLeast { get; init; } = new();

	public void Validate() {
		try {
			ValidateImpl();
		} catch (InvalidConfigurationException ex) {
			throw new InvalidConfigurationException($"Archive configuration: {ex.Message}");
		}
	}

	private void ValidateImpl() {
		if (!Enabled)
			return;

		switch (StorageType) {
			case StorageType.Unspecified:
				throw new InvalidConfigurationException("Please specify a StorageType (e.g. S3, Azure, GCP)");
			case StorageType.FileSystemDevelopmentOnly:
				FileSystem.Validate();
				break;
			case StorageType.S3:
				S3.Validate();
				break;
			case StorageType.Azure:
				Azure.Validate();
                break;
            case StorageType.GCP:
				GCP.Validate();
				break;
			default:
				throw new InvalidConfigurationException("Unknown StorageType");
		}

		RetainAtLeast.Validate();
	}
}

public enum StorageType {
	Unspecified,
	// FileSystem is for development only, it likely will not be able to reliably tell when the archiver
	// node has scavenged a chunk and replaced it.
	FileSystemDevelopmentOnly,
	S3,
	Azure,
	GCP,
}

public class FileSystemOptions {
	public string Path { get; init; } = "";

	public void Validate() {
		if (string.IsNullOrWhiteSpace(Path))
			throw new InvalidConfigurationException("Please provide a Path for the FileSystem archive");
	}
}

public class S3Options {
	public string Bucket { get; init; } = "";
	public string Region { get; init; } = "";

	public void Validate() {
		if (string.IsNullOrWhiteSpace(Bucket))
			throw new InvalidConfigurationException("Please provide a Bucket for the S3 archive");

		if (string.IsNullOrWhiteSpace(Region))
			throw new InvalidConfigurationException("Please provide a Region for the S3 archive");
	}
}

public class AzureOptions {
	public string Container { get; init; } = "";

	/// <summary>
	/// Gets or sets service URL or connection string depending on <see cref="Authentication"/> type.
	/// </summary>
	public string ConnectionStringOrServiceUrl { get; init; } = "";

	/// <summary>
	/// Gets or sets the client ID for the user-assigned managed identity.
	/// </summary>
	/// <remarks>
	/// Applicable when <see cref="Authentication"/> is <see cref="AuthenticationType.UserAssignedIdentity"/>.
	/// </remarks>
	public string UserAssignedClientId { get; init; } = "";

	/// <summary>
	/// Gets or sets the authentication type.
	/// </summary>
	public AuthenticationType Authentication { get; init; } = AuthenticationType.Unspecified;

	public void Validate() {
		string error = null;
		if (string.IsNullOrWhiteSpace(Container)) {
			error = "Please provide a Container for the Azure archive";
		} else {
			switch (Authentication) {
				case AuthenticationType.Unspecified:
					error = "Please specify an Authentication type (e.g. Default)";
					break;
				case AuthenticationType.ConnectionString:
					if (string.IsNullOrWhiteSpace(ConnectionStringOrServiceUrl))
						error = "Please provide a connection string (using ConnectionStringOrServiceUrl) for the Azure archive's storage account";
					break;
				case AuthenticationType.SystemAssignedIdentity:
					break;
				case AuthenticationType.UserAssignedIdentity:
					if (string.IsNullOrWhiteSpace(UserAssignedClientId))
						error = "Please provide a UserAssignedClientId for the Azure archive";
					break;
				case AuthenticationType.Default:
					if (string.IsNullOrWhiteSpace(ConnectionStringOrServiceUrl))
						error = "Please provide a Service URL (using ConnectionStringOrServiceUrl) for the Azure archive's storage account";
					break;
				default:
					error = "Unknown Authentication type";
					break;
			}
		}

		if (error is not null)
			throw new InvalidConfigurationException(error);
	}

	public enum AuthenticationType {
		Unspecified = 0,

		/// <summary>
		/// Combining credentials used in Azure hosting environments with credentials used in local development environment
		/// (including Azure CLI).
		/// </summary>
		/// <remarks>
		/// This type is not recommended for production use.
		/// </remarks>
		Default,

		/// <summary>
		/// System-assigned managed identity (suitable when the code is running within Azure)
		/// </summary>
		/// <seealso href="https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/system-assigned-managed-identity?"/>
		SystemAssignedIdentity,

		/// <summary>
		/// User-assigned managed identity.
		/// </summary>
		/// <seealso href="https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/user-assigned-managed-identity"/>
		UserAssignedIdentity,

		/// <summary>
		/// Uses connection string and Shared Access Signature (SAS).
		/// </summary>
		/// <seealso href="https://learn.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string"/>
		ConnectionString,
	}
}

public class GcpOptions {
	public string Bucket { get; init; } = "";

	public void Validate() {
		if (string.IsNullOrWhiteSpace(Bucket))
			throw new InvalidConfigurationException("Please provide a Bucket for the GCP archive");
	}
}

// Local chunks are removed after they have passed beyond both criteria, so they
// must both be set to be useful.
public class RetentionOptions {
	public long Days { get; init; } = TimeSpan.MaxValue.Days;
	// number of bytes in the logical log
	public long LogicalBytes { get; init; } = long.MaxValue;

	public void Validate() {
		if (Days == TimeSpan.MaxValue.Days)
			throw new InvalidConfigurationException("Please specify a value for Days to retain");

		if (LogicalBytes == long.MaxValue)
			throw new InvalidConfigurationException("Please specify a value for LogicalBytes to retain");
	}
}
