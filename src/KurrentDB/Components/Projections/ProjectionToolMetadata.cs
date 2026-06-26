// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Common.Utils;

namespace KurrentDB.Components.Projections;

// Stamps the shared projection tool-metadata convention onto Create/UpdateQuery writes so other tools
// (gaffer, CI, Navigator) can see a projection was authored via this UI. Keys are flat, top-level and
// non-$-prefixed per the convention ($ is the server's reserved namespace). Serialized as a protobuf
// Struct exactly like the gRPC front-end's ToMetadata; the engine stamps the blob onto the
// $ProjectionUpdated definition event (isPropertyMetadata) and synthesizes it back to JSON on read.
public static class ProjectionToolMetadata {
	public const string ToolName = "KurrentDB Admin UI";

	public static byte[] ForCreate() => Build("create");

	public static byte[] ForUpdate() => Build("update");

	static byte[] Build(string operation) =>
		new Struct {
			Fields = {
				["tool"] = Value.ForString(ToolName),
				["tool_version"] = Value.ForString(VersionInfo.Version),
				["operation"] = Value.ForString(operation),
			}
		}.ToByteArray();
}
