using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EventStore.Common.Utils;

namespace EventStore.Core.TransactionLog.FileNamingStrategy {
	public class VersionedPatternFileNamingStrategy : IVersionedFileNamingStrategy {
		private readonly string _path;
		private readonly string _prefix;
		private readonly Regex _pattern;

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

			return Path.Combine(_path, string.Format("{0}{1:000000}.{2:000000}", _prefix, index, version));
		}

		public string DetermineBestVersionFilenameFor(int index) {
			var allVersions = GetAllVersionsFor(index);
			if (allVersions.Length == 0)
				return GetFilenameFor(index, 0);
			int lastVersion;
			if (!int.TryParse(allVersions[0].Substring(allVersions[0].LastIndexOf('.') + 1), out lastVersion))
				throw new Exception(string.Format("Could not determine version from filename '{0}'.", allVersions[0]));
			return GetFilenameFor(index, lastVersion + 1);
		}

		public string[] GetAllVersionsFor(int index) {
			var versions = Directory.EnumerateFiles(_path, string.Format("{0}{1:000000}.*", _prefix, index))
				.Where(x => _pattern.IsMatch(Path.GetFileName(x)))
				.OrderByDescending(x => x, StringComparer.CurrentCultureIgnoreCase)
				.ToArray();
			return versions;
		}

		public int GetIndexFor(string fileName) {
			if (!_pattern.IsMatch(fileName))
				throw new ArgumentException($"Invalid file name: {fileName}");

			var start = _prefix.Length;
			var end = fileName.IndexOf('.', _prefix.Length);
			Debug.Assert(end != -1);

			if (!int.TryParse(fileName[start..end], out var fileIndex))
				throw new ArgumentException($"Invalid file name: {fileName}");

			return fileIndex;
		}

		public int GetVersionFor(string fileName) {
			if (!_pattern.IsMatch(fileName))
				throw new ArgumentException($"Invalid file name: {fileName}");

			var dot = fileName.IndexOf('.', _prefix.Length);
			Debug.Assert(dot != -1);

			if (!int.TryParse(fileName[(dot + 1)..], out var version))
				throw new ArgumentException($"Invalid file name: {fileName}");

			return version;
		}

		public string[] GetAllPresentFiles() {
			var versions = Directory.EnumerateFiles(_path, string.Format("{0}*.*", _prefix))
				.Where(x => _pattern.IsMatch(Path.GetFileName(x)))
				.ToArray();
			return versions;
		}

		public string GetTempFilename() {
			return Path.Combine(_path, string.Format("{0}.tmp", Guid.NewGuid()));
		}

		public string[] GetAllTempFiles() {
			return Directory.GetFiles(_path, "*.tmp");
		}
	}
}
