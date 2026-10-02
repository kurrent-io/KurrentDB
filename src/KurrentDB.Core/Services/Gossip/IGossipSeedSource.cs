// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;

namespace KurrentDB.Core.Services.Gossip;

public interface IGossipSeedSource {
	IAsyncResult BeginGetHostEndpoints(AsyncCallback requestCallback, object state);
	IReadOnlyList<EndPoint> EndGetHostEndpoints(IAsyncResult asyncResult);
}
