// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class versioned_pattern_filenaming_strategy : SpecificationWithDirectory {
	[Test]
	public void when_constructed_with_null_path_should_throws_argumentnullexception() {
		Assert.Throws<ArgumentNullException>(() => new VersionedPatternFileNamingStrategy(null, "prefix"));
	}

	[Test]
	public void when_constructed_with_null_prefix_should_throws_argumentnullexception() {
		Assert.Throws<ArgumentNullException>(() => new VersionedPatternFileNamingStrategy("path", null));
	}

	[Test]
	public void when_getting_file_for_positive_index_and_no_version_appends_index_to_name_with_zero_version() {
		var strategy = new VersionedPatternFileNamingStrategy("path", "prefix-");
		Assert.AreEqual("path" + Path.DirectorySeparatorChar + "prefix-000001.000000",
			strategy.GetFilenameFor(1, 0));
	}

	[Test]
	public void when_getting_file_for_nonnegative_index_and_version_appends_value_and_provided_version() {
		var strategy = new VersionedPatternFileNamingStrategy("path", "prefix-");
		Assert.AreEqual("path" + Path.DirectorySeparatorChar + "prefix-000001.000007",
			strategy.GetFilenameFor(1, 7));
	}

	[Test]
	public void when_getting_file_for_negative_index_throws_argumentoutofrangeexception() {
		var strategy = new VersionedPatternFileNamingStrategy("Path", "prefix");
		Assert.Throws<ArgumentOutOfRangeException>(() => strategy.GetFilenameFor(-1, 0));
	}

	[Test]
	public void when_getting_file_for_negative_version_throws_argumentoutofrangeexception() {
		var strategy = new VersionedPatternFileNamingStrategy("Path", "prefix");
		Assert.Throws<ArgumentOutOfRangeException>(() => strategy.GetFilenameFor(0, -1));
	}

	[Test]
	public void returns_all_existing_versions_of_the_same_chunk_in_descending_order_of_versions() {
		File.Create(GetFilePathFor("foo")).Close();
		File.Create(GetFilePathFor("bla")).Close();

		File.Create(GetFilePathFor("chunk-000001.000000")).Close();
		File.Create(GetFilePathFor("chunk-000002.000000")).Close();
		File.Create(GetFilePathFor("chunk-000003.000000")).Close();

		File.Create(GetFilePathFor("chunk-000005.000000")).Close();
		File.Create(GetFilePathFor("chunk-000005.000007")).Close();
		File.Create(GetFilePathFor("chunk-000005.000002")).Close();
		File.Create(GetFilePathFor("chunk-000005.000005")).Close();

		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		var versions = strategy.GetAllVersionsFor(5);
		Assert.AreEqual(4, versions.Length);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000007"), versions[0]);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000005"), versions[1]);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000002"), versions[2]);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000000"), versions[3]);
	}

	[Test]
	public void returns_all_existing_not_temporary_files_with_correct_pattern() {
		File.Create(GetFilePathFor("foo")).Close();
		File.Create(GetFilePathFor("bla")).Close();
		File.Create(GetFilePathFor("chunk-000001.000000.tmp")).Close();
		File.Create(GetFilePathFor("chunk-001.000")).Close();

		File.Create(GetFilePathFor("chunk-000001.000000")).Close();
		File.Create(GetFilePathFor("chunk-000002.000000")).Close();
		File.Create(GetFilePathFor("chunk-000003.000000")).Close();

		File.Create(GetFilePathFor("chunk-000005.000000")).Close();
		File.Create(GetFilePathFor("chunk-000005.000007")).Close();
		File.Create(GetFilePathFor("chunk-000005.000002")).Close();
		File.Create(GetFilePathFor("chunk-000005.000005")).Close();

		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		var versions = strategy.GetAllPresentFiles();
		Array.Sort(versions, StringComparer.CurrentCultureIgnoreCase);
		Assert.AreEqual(7, versions.Length);
		Assert.AreEqual(GetFilePathFor("chunk-000001.000000"), versions[0]);
		Assert.AreEqual(GetFilePathFor("chunk-000002.000000"), versions[1]);
		Assert.AreEqual(GetFilePathFor("chunk-000003.000000"), versions[2]);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000000"), versions[3]);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000002"), versions[4]);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000005"), versions[5]);
		Assert.AreEqual(GetFilePathFor("chunk-000005.000007"), versions[6]);
	}

	[Test]
	public void returns_all_temp_files_in_directory() {
		File.Create(GetFilePathFor("bla")).Close();
		File.Create(GetFilePathFor("bla.tmp")).Close();
		File.Create(GetFilePathFor("bla.temp")).Close();

		File.Create(GetFilePathFor("chunk-000005.000007.tmp")).Close();

		File.Create(GetFilePathFor("foo.tmp")).Close();

		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		var tempFiles = strategy.GetAllTempFiles();

		Assert.AreEqual(3, tempFiles.Length);
		Assert.Contains(GetFilePathFor("bla.tmp"), tempFiles);
		Assert.Contains(GetFilePathFor("chunk-000005.000007.tmp"), tempFiles);
		Assert.Contains(GetFilePathFor("foo.tmp"), tempFiles);
	}

	[Test]
	public void does_not_return_temp_file_that_is_named_as_chunk() {
		File.Create(GetFilePathFor("chunk-000000.000000.tmp")).Close();

		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		var tempFiles = strategy.GetAllVersionsFor(0);

		Assert.AreEqual(0, tempFiles.Length);
	}

	[Test]
	public void returns_temp_filenames_detectable_by_getalltempfiles_method() {
		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		Assert.AreEqual(0, strategy.GetAllTempFiles().Length);

		var tmp1 = strategy.CreateTempFilename();
		var tmp2 = strategy.CreateTempFilename();
		File.Create(tmp1).Close();
		File.Create(tmp2).Close();
		var tmp = new[] { tmp1, tmp2 };
		Array.Sort(tmp);

		var tempFiles = strategy.GetAllTempFiles();
		Array.Sort(tempFiles);
		Assert.AreEqual(2, tempFiles.Length);
		Assert.AreEqual(tmp[0], tempFiles[0]);
		Assert.AreEqual(tmp[1], tempFiles[1]);
	}

	[Test]
	public void returns_file_index_for_correctly_formatted_filename() {
		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		Assert.AreEqual(0, strategy.GetIndexFor("chunk-000000.000000"));
		Assert.AreEqual(1, strategy.GetIndexFor("chunk-000001.000000"));
		Assert.AreEqual(999999, strategy.GetIndexFor("chunk-999999.000000"));
	}

	[Test]
	public void returns_file_version_for_correctly_formatted_filename() {
		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		Assert.AreEqual(0, strategy.GetVersionFor("chunk-000002.000000"));
		Assert.AreEqual(1, strategy.GetVersionFor("chunk-000002.000001"));
		Assert.AreEqual(999999, strategy.GetVersionFor("chunk-000002.999999"));
	}

	[Test]
	public void throws_for_incorrectly_formatted_filename() {
		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		Assert.Throws<ArgumentException>(() => strategy.GetIndexFor("chunk-000000.a"));
		Assert.Throws<ArgumentException>(() => strategy.GetIndexFor("chunk-00000a.000000"));
		Assert.Throws<ArgumentException>(() => strategy.GetIndexFor("chunk-a.000000"));
		Assert.Throws<ArgumentException>(() => strategy.GetIndexFor("chunk-000000.00000a"));
		Assert.Throws<ArgumentException>(() => strategy.GetIndexFor("chunks-000000.00000a"));
		Assert.Throws<ArgumentException>(() => strategy.GetVersionFor("chunk-000000.a"));
		Assert.Throws<ArgumentException>(() => strategy.GetVersionFor("chunk-00000a.000000"));
		Assert.Throws<ArgumentException>(() => strategy.GetVersionFor("chunk-a.000000"));
		Assert.Throws<ArgumentException>(() => strategy.GetVersionFor("chunk-000000.00000a"));
		Assert.Throws<ArgumentException>(() => strategy.GetVersionFor("chunks-000000.00000a"));
		Assert.Throws<ArgumentException>(() => strategy.GetVersionFor("chunks-000000.000000"));
	}

	[Test]
	public void returns_correct_prefix() {
		var strategy = new VersionedPatternFileNamingStrategy(PathName, "chunk-");
		Assert.AreEqual("chunk-", strategy.Prefix);
	}
}
