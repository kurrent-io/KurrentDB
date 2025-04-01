// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace EventStore.Security.EncryptionAtRest.MasterKeySources.File;

public class FileSource(string path) : IMasterKeySource {
	private static readonly ILogger _logger = Log.ForContext<FileSource>();

	public string Name => "File";
	private readonly string _path = VerifyPath(path);
	private const string KeyFileExtension = ".esdb.master.key";

	public IReadOnlyList<MasterKey> LoadMasterKeys() {
		var masterKeyFiles = Directory
			.EnumerateFiles(_path, $"*{KeyFileExtension}")
			.Where(HasValidKeyId)
			.ToArray();

		var masterKeys = new List<MasterKey>(masterKeyFiles.Length);
		masterKeys.AddRange(
			masterKeyFiles
				.Select(ReadMasterKey)
				.OrderBy(x => x.Id));

		return masterKeys;
	}

	private static bool HasValidKeyId(string keyFilePath) => GetKeyId(keyFilePath) > 0;

	private static int GetKeyId(string keyFilePath) {
		var keyFileName = Path.GetFileName(keyFilePath);
		var dotIndex = keyFileName.IndexOf('.');
		if (dotIndex < 0)
			return -1;

		if (!int.TryParse(keyFileName[..dotIndex], out var keyId))
			return -1;

		return keyId;
	}

	private MasterKey ReadMasterKey(string file) {
		var lines = System.IO.File.ReadAllLines(file);
		if (lines[0]  != "-----BEGIN ESDB MASTER KEY-----" ||
		    lines[^1] != "-----END ESDB MASTER KEY-----")
			throw new Exception($"Invalid master key file: {Path.GetFileName(file)}");

		var keyId = GetKeyId(file);
		var key = Convert.FromBase64String(string.Join(string.Empty, lines[1..^1]));

		_logger.Information($"Encryption-At-Rest: ({Name}) Loaded master key: {keyId} ({key.Length * 8} bits)");
		return new MasterKey(keyId, key);
	}

	private static string VerifyPath(string path) {
		try {
			path = Path.GetFullPath(path);
		} catch {
			throw new Exception($"The specified path '{path}' is invalid.");
		}

		if (!Path.Exists(path))
			throw new Exception($"The specified path '{path}' does not exist.");

		var dirInfo = new DirectoryInfo(path);
		if (!dirInfo.Exists)
			throw new Exception($"The specified path '{path}' is not a directory.");

		return path;
	}
}
