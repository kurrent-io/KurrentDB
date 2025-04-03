// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using EventStore.Core.Services.Archive;
using Xunit.Sdk;

namespace EventStore.Core.XUnit.Tests.Services.Archive.Storage;

// if no symbol is required for the storage type then run the test (or not, according to skip).
// else a symbol is required
//    if required symbol is defined then check the prereqs, which throws if they are not present
//    else the required symbol is not defined -> skip
public static class StorageData {
	public abstract class RemoteStorageDataAttribute : DataAttribute {
		private readonly StorageType _storageType;
		private readonly object[] _extraArgs;

		protected RemoteStorageDataAttribute(StorageType storageType, object[] args, string symbol, PrerequisiteChecker checker) {
			var symbolSet = false;
			checker(ref symbolSet);
			if (!symbolSet)
				Skip = $"This remote storage test is disabled. Enable with {symbol} symbol.";

			_extraArgs = args;
			_storageType = storageType;
		}

		public sealed override IEnumerable<object[]> GetData(MethodInfo testMethod) => [[_storageType, .._extraArgs]];

		protected delegate void PrerequisiteChecker(ref bool symbolSet);
	}


	public sealed class S3Attribute(params object[] args) : RemoteStorageDataAttribute(
		StorageType.S3,
		args,
		Symbol,
		static (ref bool isSet) => CheckPrerequisites(ref isSet)) {
		const string Symbol = "RUN_S3_TESTS";

		[Conditional(Symbol)]
		private static void CheckPrerequisites(ref bool symbolSet) {
			symbolSet = true;
			const string awsDirectoryName = ".aws";
			var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			homeDir = Path.Combine(homeDir, awsDirectoryName);
			if (!Directory.Exists(homeDir))
				throw new AwsCliDirectoryNotFoundException(homeDir);
		}
	}

	public sealed class FileSystemAttribute(params object[] args) : RemoteStorageDataAttribute(
		StorageType.FileSystemDevelopmentOnly,
		args,
		string.Empty,
		static (ref bool isSet) => isSet = true) {
	}
}
