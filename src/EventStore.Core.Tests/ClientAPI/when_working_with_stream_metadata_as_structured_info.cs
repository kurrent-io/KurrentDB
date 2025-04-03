// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using ExpectedVersion = EventStore.ClientAPI.ExpectedVersion;
using StreamMetadata = EventStore.ClientAPI.StreamMetadata;

namespace EventStore.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_working_with_stream_metadata_as_structured_info<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private MiniNode<TLogFormat, TStreamId> _node;
	private IEventStoreConnection _connection;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_node = new MiniNode<TLogFormat, TStreamId>(PathName);
		await _node.Start();

		_connection = BuildConnection(_node);
		await _connection.ConnectAsync();
	}

	protected virtual IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	[OneTimeTearDown]
	public override async Task TestFixtureTearDown() {
		_connection.Close();
		await _node.Shutdown();
		await base.TestFixtureTearDown();
	}

	[Test]
	public async Task setting_empty_metadata_works() {
		const string stream = "setting_empty_metadata_works";

		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, StreamMetadata.Create());

		var meta = await _connection.GetStreamMetadataAsRawBytesAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);
		Assert.AreEqual(Helper.UTF8NoBom.GetBytes("{}"), meta.StreamMetadata);
	}

	[Test]
	public async Task setting_metadata_few_times_returns_last_metadata_info() {
		const string stream = "setting_metadata_few_times_returns_last_metadata_info";
		var metadata =
			StreamMetadata.Create(17, TimeSpan.FromSeconds(0xDEADBEEF), 10, TimeSpan.FromSeconds(0xABACABA));
		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, metadata);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);
		Assert.AreEqual(metadata.MaxCount, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(metadata.MaxAge, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(metadata.TruncateBefore, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(metadata.CacheControl, meta.StreamMetadata.CacheControl);

		metadata = StreamMetadata.Create(37, TimeSpan.FromSeconds(0xBEEFDEAD), 24,
			TimeSpan.FromSeconds(0xDABACABAD));
		await _connection.SetStreamMetadataAsync(stream, 0, metadata);

		meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(1, meta.MetastreamVersion);
		Assert.AreEqual(metadata.MaxCount, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(metadata.MaxAge, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(metadata.TruncateBefore, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(metadata.CacheControl, meta.StreamMetadata.CacheControl);
	}

	[Test]
	public async Task trying_to_set_metadata_with_wrong_expected_version_fails() {
		const string stream = "trying_to_set_metadata_with_wrong_expected_version_fails";
		await AssertEx.ThrowsAsync<WrongExpectedVersionException>(() =>
			_connection.SetStreamMetadataAsync(stream, 2, StreamMetadata.Create()));
	}

	[Test]
	public async Task setting_metadata_with_expected_version_any_works() {
		const string stream = "setting_metadata_with_expected_version_any_works";
		var metadata =
			StreamMetadata.Create(17, TimeSpan.FromSeconds(0xDEADBEEF), 10, TimeSpan.FromSeconds(0xABACABA));
		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.Any, metadata);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);
		Assert.AreEqual(metadata.MaxCount, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(metadata.MaxAge, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(metadata.TruncateBefore, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(metadata.CacheControl, meta.StreamMetadata.CacheControl);

		metadata = StreamMetadata.Create(37, TimeSpan.FromSeconds(0xBEEFDEAD), 24,
			TimeSpan.FromSeconds(0xDABACABAD));
		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.Any, metadata);

		meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(1, meta.MetastreamVersion);
		Assert.AreEqual(metadata.MaxCount, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(metadata.MaxAge, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(metadata.TruncateBefore, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(metadata.CacheControl, meta.StreamMetadata.CacheControl);
	}

	[Test]
	public async Task setting_metadata_for_not_existing_stream_works() {
		const string stream = "setting_metadata_for_not_existing_stream_works";
		var metadata =
			StreamMetadata.Create(17, TimeSpan.FromSeconds(0xDEADBEEF), 10, TimeSpan.FromSeconds(0xABACABA));
		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, metadata);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);
		Assert.AreEqual(metadata.MaxCount, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(metadata.MaxAge, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(metadata.TruncateBefore, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(metadata.CacheControl, meta.StreamMetadata.CacheControl);
	}

	[Test]
	public async Task setting_metadata_for_existing_stream_works() {
		const string stream = "setting_metadata_for_existing_stream_works";

		await _connection.AppendToStreamAsync(stream, ExpectedVersion.NoStream, TestEvent.NewTestEvent());

		var metadata =
			StreamMetadata.Create(17, TimeSpan.FromSeconds(0xDEADBEEF), 10, TimeSpan.FromSeconds(0xABACABA));
		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, metadata);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);
		Assert.AreEqual(metadata.MaxCount, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(metadata.MaxAge, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(metadata.TruncateBefore, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(metadata.CacheControl, meta.StreamMetadata.CacheControl);
	}

	[Test]
	public async Task getting_metadata_for_nonexisting_stream_returns_empty_stream_metadata() {
		const string stream = "getting_metadata_for_nonexisting_stream_returns_empty_stream_metadata";

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(-1, meta.MetastreamVersion);
		Assert.AreEqual(null, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(null, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(null, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(null, meta.StreamMetadata.CacheControl);
	}

	[Test, Ignore("You can't get stream metadata for metastream through ClientAPI")]
	public async Task getting_metadata_for_metastream_returns_correct_metadata() {
		const string stream = "$$getting_metadata_for_metastream_returns_correct_metadata";

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(-1, meta.MetastreamVersion);
		Assert.AreEqual(1, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(null, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(null, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(null, meta.StreamMetadata.CacheControl);
	}

	[Test]
	public async Task getting_metadata_for_deleted_stream_returns_empty_stream_metadata_and_signals_stream_deletion() {
		const string stream =
			"getting_metadata_for_deleted_stream_returns_empty_stream_metadata_and_signals_stream_deletion";

		var metadata =
			StreamMetadata.Create(17, TimeSpan.FromSeconds(0xDEADBEEF), 10, TimeSpan.FromSeconds(0xABACABA));
		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, metadata);

		await _connection.DeleteStreamAsync(stream, ExpectedVersion.NoStream, hardDelete: true);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(true, meta.IsStreamDeleted);
		Assert.AreEqual(EventNumber.DeletedStream, meta.MetastreamVersion);
		Assert.AreEqual(null, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(null, meta.StreamMetadata.MaxAge);
		Assert.AreEqual(null, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(null, meta.StreamMetadata.CacheControl);
		Assert.AreEqual(null, meta.StreamMetadata.Acl);
	}

	[Test]
	public async Task setting_correctly_formatted_metadata_as_raw_allows_to_read_it_as_structured_metadata() {
		const string stream =
			"setting_correctly_formatted_metadata_as_raw_allows_to_read_it_as_structured_metadata";

		var rawMeta = Helper.UTF8NoBom.GetBytes(@"{
                                                           ""$maxCount"": 17,
                                                           ""$maxAge"": 123321,
                                                           ""$tb"": 23,
                                                           ""$cacheControl"": 7654321,
                                                           ""$acl"": {
                                                               ""$r"": ""readRole"",
                                                               ""$w"": ""writeRole"",
                                                               ""$d"": ""deleteRole"",
                                                               ""$mw"": ""metaWriteRole""
                                                           },
                                                           ""customString"": ""a string"",
                                                           ""customInt"": -179,
                                                           ""customDouble"": 1.7,
                                                           ""customLong"": 123123123123123123,
                                                           ""customBool"": true,
                                                           ""customNullable"": null,
                                                           ""customRawJson"": {
                                                               ""subProperty"": 999
                                                           }
                                                      }");

		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, rawMeta);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);
		Assert.AreEqual(17, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(TimeSpan.FromSeconds(123321), meta.StreamMetadata.MaxAge);
		Assert.AreEqual(23, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(TimeSpan.FromSeconds(7654321), meta.StreamMetadata.CacheControl);

		Assert.NotNull(meta.StreamMetadata.Acl);
		Assert.AreEqual("readRole", meta.StreamMetadata.Acl.ReadRole);
		Assert.AreEqual("writeRole", meta.StreamMetadata.Acl.WriteRole);
		Assert.AreEqual("deleteRole", meta.StreamMetadata.Acl.DeleteRole);
		// meta role removed to allow reading
		//            Assert.AreEqual("metaReadRole", meta.StreamMetadata.Acl.MetaReadRole);
		Assert.AreEqual("metaWriteRole", meta.StreamMetadata.Acl.MetaWriteRole);

		Assert.AreEqual("a string", meta.StreamMetadata.GetValue<string>("customString"));
		Assert.AreEqual(-179, meta.StreamMetadata.GetValue<int>("customInt"));
		Assert.AreEqual(1.7, meta.StreamMetadata.GetValue<double>("customDouble"));
		Assert.AreEqual(123123123123123123L, meta.StreamMetadata.GetValue<long>("customLong"));
		Assert.AreEqual(true, meta.StreamMetadata.GetValue<bool>("customBool"));
		Assert.AreEqual(null, meta.StreamMetadata.GetValue<int?>("customNullable"));
		Assert.AreEqual(@"{""subProperty"":999}", meta.StreamMetadata.GetValueAsRawJsonString("customRawJson"));
	}

	[Test]
	public async Task setting_structured_metadata_with_custom_properties_returns_them_untouched() {
		const string stream = "setting_structured_metadata_with_custom_properties_returns_them_untouched";

		StreamMetadata metadata = StreamMetadata.Build()
			.SetMaxCount(17)
			.SetMaxAge(TimeSpan.FromSeconds(123321))
			.SetTruncateBefore(23)
			.SetCacheControl(TimeSpan.FromSeconds(7654321))
			.SetReadRole("readRole")
			.SetWriteRole("writeRole")
			.SetDeleteRole("deleteRole")
			//.SetMetadataReadRole("metaReadRole")
			.SetMetadataWriteRole("metaWriteRole")
			.SetCustomProperty("customString", "a string")
			.SetCustomProperty("customInt", -179)
			.SetCustomProperty("customDouble", 1.7)
			.SetCustomProperty("customLong", 123123123123123123L)
			.SetCustomProperty("customBool", true)
			.SetCustomProperty("customNullable", new int?())
			.SetCustomPropertyWithValueAsRawJsonString("customRawJson",
				@"{
                                                                                                       ""subProperty"": 999
                                                                                                 }");

		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, metadata);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);
		Assert.AreEqual(17, meta.StreamMetadata.MaxCount);
		Assert.AreEqual(TimeSpan.FromSeconds(123321), meta.StreamMetadata.MaxAge);
		Assert.AreEqual(23, meta.StreamMetadata.TruncateBefore);
		Assert.AreEqual(TimeSpan.FromSeconds(7654321), meta.StreamMetadata.CacheControl);

		Assert.NotNull(meta.StreamMetadata.Acl);
		Assert.AreEqual("readRole", meta.StreamMetadata.Acl.ReadRole);
		Assert.AreEqual("writeRole", meta.StreamMetadata.Acl.WriteRole);
		Assert.AreEqual("deleteRole", meta.StreamMetadata.Acl.DeleteRole);
		//Assert.AreEqual("metaReadRole", meta.StreamMetadata.Acl.MetaReadRole);
		Assert.AreEqual("metaWriteRole", meta.StreamMetadata.Acl.MetaWriteRole);

		Assert.AreEqual("a string", meta.StreamMetadata.GetValue<string>("customString"));
		Assert.AreEqual(-179, meta.StreamMetadata.GetValue<int>("customInt"));
		Assert.AreEqual(1.7, meta.StreamMetadata.GetValue<double>("customDouble"));
		Assert.AreEqual(123123123123123123L, meta.StreamMetadata.GetValue<long>("customLong"));
		Assert.AreEqual(true, meta.StreamMetadata.GetValue<bool>("customBool"));
		Assert.AreEqual(null, meta.StreamMetadata.GetValue<int?>("customNullable"));
		Assert.AreEqual(@"{""subProperty"":999}", meta.StreamMetadata.GetValueAsRawJsonString("customRawJson"));
	}

	[Test]
	public async Task setting_structured_metadata_with_multiple_roles_can_be_read_back() {
		const string stream = "setting_structured_metadata_with_multiple_roles_can_be_read_back";

		StreamMetadata metadata = StreamMetadata.Build()
			.SetReadRoles(new[] { "r1", "r2", "r3" })
			.SetWriteRoles(new[] { "w1", "w2" })
			.SetDeleteRoles(new[] { "d1", "d2", "d3", "d4" })
			.SetMetadataWriteRoles(new[] { "mw1", "mw2" });

		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, metadata);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);

		Assert.NotNull(meta.StreamMetadata.Acl);
		Assert.AreEqual(new[] { "r1", "r2", "r3" }, meta.StreamMetadata.Acl.ReadRoles);
		Assert.AreEqual(new[] { "w1", "w2" }, meta.StreamMetadata.Acl.WriteRoles);
		Assert.AreEqual(new[] { "d1", "d2", "d3", "d4" }, meta.StreamMetadata.Acl.DeleteRoles);
		Assert.AreEqual(new[] { "mw1", "mw2" }, meta.StreamMetadata.Acl.MetaWriteRoles);
	}

	[Test]
	public async Task setting_correct_metadata_with_multiple_roles_in_acl_allows_to_read_it_as_structured_metadata() {
		const string stream =
			"setting_correct_metadata_with_multiple_roles_in_acl_allows_to_read_it_as_structured_metadata";

		var rawMeta = Helper.UTF8NoBom.GetBytes(@"{
                                                           ""$acl"": {
                                                               ""$r"": [""r1"", ""r2"", ""r3""],
                                                               ""$w"": [""w1"", ""w2""],
                                                               ""$d"": [""d1"", ""d2"", ""d3"", ""d4""],
                                                               ""$mw"": [""mw1"", ""mw2""],
                                                           }
                                                      }");

		await _connection.SetStreamMetadataAsync(stream, ExpectedVersion.NoStream, rawMeta);

		var meta = await _connection.GetStreamMetadataAsync(stream);
		Assert.AreEqual(stream, meta.Stream);
		Assert.AreEqual(false, meta.IsStreamDeleted);
		Assert.AreEqual(0, meta.MetastreamVersion);

		Assert.NotNull(meta.StreamMetadata.Acl);
		Assert.AreEqual(new[] { "r1", "r2", "r3" }, meta.StreamMetadata.Acl.ReadRoles);
		Assert.AreEqual(new[] { "w1", "w2" }, meta.StreamMetadata.Acl.WriteRoles);
		Assert.AreEqual(new[] { "d1", "d2", "d3", "d4" }, meta.StreamMetadata.Acl.DeleteRoles);
		Assert.AreEqual(new[] { "mw1", "mw2" }, meta.StreamMetadata.Acl.MetaWriteRoles);
	}
}
