// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using EventStore.Core.Util;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexVAny;

[TestFixture]
public class when_opening_v1_indexmap : SpecificationWithDirectoryPerTestFixture {
	private const string
		V1FileContents =
			"6954492497DD64BF751FC6CFA0C9CEE2\r\n1\r\n-1/-1\r\n"; //this doesn't matter because the hash will be calculated from the bytes

	//The hash value will change depending on whether the file was written on windows or some other platform
	private string V2FileContents = Environment.NewLine + "2" + Environment.NewLine + "-1/-1" +
									Environment.NewLine + "4" + Environment.NewLine;

	private string _filename;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		var ms = new MemoryStream(Encoding.UTF8.GetBytes(V2FileContents));
		var md5 = MD5Hash.GetHashFor(ms);
		V2FileContents = BitConverter.ToString(md5).Replace("-", "") + V2FileContents;
		await base.TestFixtureSetUp();

		_filename = GetFilePathFor("indexfile");
		File.WriteAllText(_filename, V1FileContents);
	}

	[Test]
	public void should_initialize_auto_merge_level_correctly() {
		var map = IndexMapTestFactory.FromFile(_filename, loadPTables: false, maxAutoMergeLevel: 4);

		var v2File = GetFilePathFor("v1tov2");
		map.SaveToFile(v2File);
		Assert.AreEqual(V2FileContents, File.ReadAllText(v2File));
	}
}

[TestFixture]
public class when_opening_indexmap_with_auto_merge_level_set : SpecificationWithDirectoryPerTestFixture {
	private string _filename;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_filename = GetFilePathFor("indexfile");
		var empty = IndexMap.CreateEmpty(2, 4, Constants.PTableMaxReaderCountDefault);
		empty.SaveToFile(_filename);
	}

	[Test]
	public void should_throw_if_max_auto_merge_is_larger_than_map_value() {
		Assert.Throws<CorruptIndexException>(() => IndexMapTestFactory.FromFile(_filename, maxAutoMergeLevel: 5));
	}

	[Test]
	public void can_reduce_max_auto_merge_to_lower_than_map_value() {
		IndexMap map = null;
		Assert.DoesNotThrow(() => map = IndexMapTestFactory.FromFile(_filename, maxAutoMergeLevel: 3));
		var newIndexFile = GetFilePathFor("indexfile2");
		map?.SaveToFile(newIndexFile);
		var lines = File.ReadAllLines(newIndexFile);
		Assert.AreEqual(lines[3], "3");
	}

	[Test]
	public void should_open_if_auto_merge_levels_match() {
		Assert.DoesNotThrow(() => IndexMapTestFactory.FromFile(_filename, maxAutoMergeLevel: 4));
	}
}
