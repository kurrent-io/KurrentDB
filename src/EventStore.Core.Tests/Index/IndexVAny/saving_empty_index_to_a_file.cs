// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Core.Index;
using EventStore.Core.Util;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.IndexVAny;

[TestFixture]
public class saving_empty_index_to_a_file : SpecificationWithDirectoryPerTestFixture {
	private string _filename;
	private IndexMap _map;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_filename = GetFilePathFor("indexfile");
		_map = IndexMapTestFactory.FromFile(_filename);
		_map.SaveToFile(_filename);
	}

	[Test]
	public void the_file_exists() {
		Assert.IsTrue(File.Exists(_filename));
	}

	[Test]
	public void the_file_contains_correct_data() {
		using (var fs = File.OpenRead(_filename))
		using (var reader = new StreamReader(fs)) {
			var text = reader.ReadToEnd();
			var lines = text.Replace("\r", "").Split('\n');

			fs.Position = 32;
			var md5 = MD5Hash.GetHashFor(fs);
			var md5String = BitConverter.ToString(md5).Replace("-", "");

			Assert.AreEqual(5, lines.Count());
			Assert.AreEqual(md5String, lines[0]);
			Assert.AreEqual(IndexMap.IndexMapVersion.ToString(), lines[1]);
			Assert.AreEqual("-1/-1", lines[2]);
			Assert.AreEqual($"{int.MaxValue}", lines[3]);
			Assert.AreEqual("", lines[4]);
		}
	}

	[Test]
	public void saved_file_could_be_read_correctly_and_without_errors() {
		var map = IndexMapTestFactory.FromFile(_filename);

		Assert.AreEqual(-1, map.PrepareCheckpoint);
		Assert.AreEqual(-1, map.CommitCheckpoint);
	}
}
