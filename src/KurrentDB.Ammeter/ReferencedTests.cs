// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Api.Tests.Streams;

namespace KurrentDB.Ammeter;

//qq sometimes the streams tests are discovered even without this class
// maybe this will sort itself out when we have ironed out VSTest vs MTP
[InheritsTests]
public class StreamsTests : StreamsServiceTests { }
