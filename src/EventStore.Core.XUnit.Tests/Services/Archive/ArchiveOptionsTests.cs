// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Common.Exceptions;
using EventStore.Core.Services.Archive;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Services.Archive;
public class ArchiveOptionsTests {
	[Fact]
	public void default_is_valid() {
		var sut = new ArchiveOptions();
		sut.Validate();
	}

	[Fact]
	public void unspecified_storage_type_is_invalid() {
		var sut = new ArchiveOptions {
			Enabled = true,
			StorageType = StorageType.Unspecified,
		};
		var ex = Assert.Throws<InvalidConfigurationException>(sut.Validate);
		Assert.Contains("StorageType", ex.Message);
	}

	[Fact]
	public void can_use_file_system_options() {
		var sut = new ArchiveOptions {
			Enabled = true,
			RetainAtLeast = new() {
				Days = 5,
				 LogicalBytes = 500,
			},
			StorageType = StorageType.FileSystemDevelopmentOnly,
			FileSystem = new() {
				Path = "c:/archive",
			},
		};
		sut.Validate();
	}

	[Fact]
	public void can_use_s3_options() {
		var sut = new ArchiveOptions {
			Enabled = true,
			RetainAtLeast = new() {
				Days = 5,
				LogicalBytes = 500,
			},
			StorageType = StorageType.S3,
			S3 = new() {
				Bucket = "bouquet",
				Region = "the-region",
			},
		};
		sut.Validate();
	}

	[Fact]
	public void file_system_options_require_path() {
		var sut = new ArchiveOptions {
			Enabled = true,
			StorageType = StorageType.FileSystemDevelopmentOnly,
		};
		var ex = Assert.Throws<InvalidConfigurationException>(sut.Validate);
		Assert.Contains("Path", ex.Message);
	}

	[Fact]
	public void s3_options_require_bucket() {
		var sut = new ArchiveOptions {
			Enabled = true,
			StorageType = StorageType.S3,
			S3 = new() {
				Region = "the-region",
			}
		};
		var ex = Assert.Throws<InvalidConfigurationException>(sut.Validate);
		Assert.Contains("Bucket", ex.Message);
	}

	[Fact]
	public void s3_options_require_region() {
		var sut = new ArchiveOptions {
			Enabled = true,
			StorageType = StorageType.S3,
			S3 = new() {
				Bucket = "the-bucket",
			}
		};
		var ex = Assert.Throws<InvalidConfigurationException>(sut.Validate);
		Assert.Contains("Region", ex.Message);
	}

	[Fact]
	public void retention_options_require_days() {
		var sut = new ArchiveOptions {
			Enabled = true,
			StorageType = StorageType.FileSystemDevelopmentOnly,
			FileSystem = new() {
				Path = "c:/archive",
			},
			RetainAtLeast = new() {
				LogicalBytes = 50,
			}
		};
		var ex = Assert.Throws<InvalidConfigurationException>(sut.Validate);
		Assert.Contains("Days", ex.Message);
	}

	[Fact]
	public void retention_options_require_logical_bytes() {
		var sut = new ArchiveOptions {
			Enabled = true,
			StorageType = StorageType.FileSystemDevelopmentOnly,
			FileSystem = new() {
				Path = "c:/archive",
			},
			RetainAtLeast = new() {
				Days = 50,
			}
		};
		var ex = Assert.Throws<InvalidConfigurationException>(sut.Validate);
		Assert.Contains("LogicalBytes", ex.Message);
	}
}
