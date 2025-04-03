// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net;
using EventStore.Common.Utils;

namespace EventStore.Core.Data;

public class VNodeInfo {
	public readonly Guid InstanceId;
	public readonly int DebugIndex;
	public readonly IPEndPoint InternalTcp;
	public readonly IPEndPoint InternalSecureTcp;
	public readonly IPEndPoint ExternalTcp;
	public readonly IPEndPoint ExternalSecureTcp;
	public readonly EndPoint HttpEndPoint;
	public readonly bool IsReadOnlyReplica;

	public VNodeInfo(Guid instanceId, int debugIndex,
		IPEndPoint internalTcp, IPEndPoint internalSecureTcp,
		IPEndPoint externalTcp, IPEndPoint externalSecureTcp,
		EndPoint httpEndPoint,
		bool isReadOnlyReplica) {
		Ensure.NotEmptyGuid(instanceId, "instanceId");
		Ensure.Equal(false, internalTcp == null && internalSecureTcp == null, "Both internal TCP endpoints are null");
		Ensure.NotNull(httpEndPoint, nameof(httpEndPoint));

		DebugIndex = debugIndex;
		InstanceId = instanceId;
		InternalTcp = internalTcp;
		InternalSecureTcp = internalSecureTcp;
		ExternalTcp = externalTcp;
		ExternalSecureTcp = externalSecureTcp;
		HttpEndPoint = httpEndPoint;
		IsReadOnlyReplica = isReadOnlyReplica;
	}

	public bool Is(EndPoint endPoint) {
		return endPoint != null
		       && HttpEndPoint.Equals(endPoint)
		           || (InternalTcp != null && InternalTcp.Equals(endPoint))
		           || (InternalSecureTcp != null && InternalSecureTcp.Equals(endPoint))
		           || (ExternalTcp != null && ExternalTcp.Equals(endPoint))
		           || (ExternalSecureTcp != null && ExternalSecureTcp.Equals(endPoint));
	}

	public override string ToString() {
		return string.Format("InstanceId: {0:B}, InternalTcp: {1}, InternalSecureTcp: {2}, " +
		                     "ExternalTcp: {3}, ExternalSecureTcp: {4}, HttpEndPoint: {5}," +
							 "IsReadOnlyReplica: {6}",
			InstanceId,
			InternalTcp,
			InternalSecureTcp,
			ExternalTcp,
			ExternalSecureTcp,
			HttpEndPoint,
			IsReadOnlyReplica);
	}
}
