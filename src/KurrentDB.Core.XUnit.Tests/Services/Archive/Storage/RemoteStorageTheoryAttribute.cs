// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using KurrentDB.Core.Services.Archive;
using Xunit.Sdk;

namespace KurrentDB.Core.XUnit.Tests.Services.Archive.Storage;

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

		public sealed override IEnumerable<object[]> GetData(MethodInfo testMethod) => [[_storageType, .. _extraArgs]];

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

	public sealed class GCPAttribute(params object[] args) : RemoteStorageDataAttribute(
		StorageType.GCP,
		args,
		Symbol,
		static (ref bool isSet) => CheckPrerequisites(ref isSet)) {
		const string Symbol = "RUN_GCP_TESTS";

		[Conditional(Symbol)]
		private static void CheckPrerequisites(ref bool symbolSet) {
			symbolSet = true;
			const string gcpDirectoryNameLinux = ".config/gcloud";
			const string gcpDirectoryNameWindows = "gcloud";
			var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			if (OperatingSystem.IsLinux())
				homeDir = Path.Combine(homeDir, gcpDirectoryNameLinux);
			else if (OperatingSystem.IsWindows())
				homeDir = Path.Combine(homeDir, gcpDirectoryNameWindows);
			else
				throw new NotSupportedException();

			if (!Directory.Exists(homeDir))
				throw new GcpCliDirectoryNotFoundException(homeDir);
		}
	}

	public sealed class FileSystemAttribute(params object[] args) : RemoteStorageDataAttribute(
		StorageType.FileSystemDevelopmentOnly,
		args,
		string.Empty,
		static (ref bool isSet) => isSet = true);

	public sealed class AzureAttribute(params object[] args) : RemoteStorageDataAttribute(
		StorageType.Azure,
		args,
		Symbol,
		static (ref bool isSet) => CheckPrerequisites(ref isSet)) {
		private const string Symbol = "RUN_AZ_TESTS";

		[Conditional(Symbol)]
		private static void CheckPrerequisites(ref bool symbolSet) {
			symbolSet = true;

			CheckAzuriteLocalEndPoint();
		}

		private static void CheckAzuriteLocalEndPoint() {
			var azuriteBlobEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 10_000);

			var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
			try {
				socket.Connect(azuriteBlobEndPoint);
			} catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionRefused) {
				throw new AzuriteNotStartedException();
			} finally {
				socket.Dispose();
			}
		}
	}
}
