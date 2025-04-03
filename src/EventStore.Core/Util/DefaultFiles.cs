// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Runtime;
using EventStore.Common.Utils;

namespace EventStore.Core.Util;

public static class DefaultFiles {
    public static readonly string DefaultConfigFile = RuntimeInformation.IsWindows
        ? string.Empty
        : "kurrentdb.conf";

    public static readonly string LegacyEventStoreConfigFile = RuntimeInformation.IsWindows
	    ? string.Empty
	    : "eventstore.conf";

    public static string DefaultConfigPath =>
	    Path.Combine(Locations.DefaultConfigurationDirectory, DefaultConfigFile);
    public static string LegacyEventStoreConfigPath =>
	    Path.Combine(Locations.LegacyConfigurationDirectory, LegacyEventStoreConfigFile);
}
