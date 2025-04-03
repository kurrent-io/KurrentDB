// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Text.RegularExpressions;
using EventStore.Common.Utils;
using System.Linq;

namespace EventStore.Core.TransactionLog.FileNamingStrategy;

public class VersionedPatternFileNamingStrategy : IVersionedFileNamingStrategy {
	private readonly string _path;
	private readonly string _prefix;
	private readonly Regex _pattern;

	public string Prefix => _prefix;

	public VersionedPatternFileNamingStrategy(string path, string prefix) {
		Ensure.NotNull(path, "path");
		Ensure.NotNull(prefix, "prefix");
		_path = path;
		_prefix = prefix;

		_pattern = new Regex("^" + _prefix + @"\d{6}\.\d{6}$");
	}

	public string GetFilenameFor(int index, int version) {
		Ensure.Nonnegative(index, "index");
		Ensure.Nonnegative(version, "version");

		return Path.Combine(_path, $"{_prefix}{index:000000}.{version:000000}");
	}

	public string DetermineNewVersionFilenameForIndex(int index, int defaultVersion) {
		var allVersions = GetAllVersionsFor(index);

		if (allVersions.Length == 0)
			return GetFilenameFor(index, defaultVersion);

		var lastFile = allVersions[0];
		var lastVersionSpan = lastFile.AsSpan(lastFile.LastIndexOf('.') + 1);
		if (!int.TryParse(lastVersionSpan, out var lastVersion))
			throw new Exception($"Could not determine version from filename '{lastFile}'.");

		return GetFilenameFor(index, lastVersion + 1);
	}

	public string[] GetAllVersionsFor(int index) {
		var versions = Directory.EnumerateFiles(_path, $"{_prefix}{index:000000}.*")
			.Where(x => _pattern.IsMatch(Path.GetFileName(x)))
			.OrderByDescending(x => x, StringComparer.CurrentCultureIgnoreCase)
			.ToArray();
		return versions;
	}

	public int GetIndexFor(ReadOnlySpan<char> fileName) {
		if (!_pattern.IsMatch(fileName))
			throw new ArgumentException($"Invalid file name: {fileName}");

		var start = _prefix.Length;
		var end = fileName.Slice(_prefix.Length).IndexOf('.');

		if (end < 0 || !int.TryParse(fileName[start..(end + _prefix.Length)], out var fileIndex))
			throw new ArgumentException($"Invalid file name: {fileName}");

		return fileIndex;
	}

	public int GetVersionFor(ReadOnlySpan<char> fileName) {
		if (!_pattern.IsMatch(fileName))
			throw new ArgumentException($"Invalid file name: {fileName}");

		var dot = fileName.Slice(_prefix.Length).IndexOf('.');

		if (dot < 0 || !int.TryParse(fileName[(dot + 1 + _prefix.Length)..], out var version))
			throw new ArgumentException($"Invalid file name: {fileName}");

		return version;
	}

	public string[] GetAllPresentFiles() {
		var versions = Directory
			.EnumerateFiles(_path, $"{_prefix}*.*")
			.Where(x => _pattern.IsMatch(Path.GetFileName(x)))
			.ToArray();
		return versions;
	}

	public string CreateTempFilename() {
		return Path.Combine(_path, $"{Guid.NewGuid()}.tmp");
	}

	public string[] GetAllTempFiles() {
		return Directory.GetFiles(_path, "*.tmp");
	}
}
