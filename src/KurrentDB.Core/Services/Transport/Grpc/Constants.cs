// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Services.Transport.Grpc;

public static class Constants {
	public static class Exceptions {
		public const string ExceptionKey = "exception";

		public const string AccessDenied = "access-denied";
		public const string InvalidTransaction = "invalid-transaction";
		public const string StreamDeleted = "stream-deleted";
		public const string WrongExpectedVersion = "wrong-expected-version";
		public const string StreamNotFound = "stream-not-found";
		public const string MaximumAppendSizeExceeded = "maximum-append-size-exceeded";
		public const string MaximumAppendEventSizeExceeded = "maximum-append-event-size-exceeded";
		public const string MissingRequiredMetadataProperty = "missing-required-metadata-property";
		public const string NotLeader = "not-leader";

		public const string PersistentSubscriptionFailed = "persistent-subscription-failed";
		public const string PersistentSubscriptionDoesNotExist = "persistent-subscription-does-not-exist";
		public const string PersistentSubscriptionExists = "persistent-subscription-exists";
		public const string MaximumSubscribersReached = "maximum-subscribers-reached";
		public const string PersistentSubscriptionDropped = "persistent-subscription-dropped";

		public const string UserNotFound = "user-not-found";
		public const string UserConflict = "user-conflict";

		public const string ScavengeNotFound = "scavenge-not-found";

		public const string RedactionLockFailed = "redaction-lock-failed";
		public const string RedactionGetEventPositionFailed = "redaction-get-event-position-failed";
		public const string RedactionSwitchChunkFailed = "redaction-switch-chunk-failed";

		public const string ExpectedVersion = "expected-version";
		public const string ActualVersion = "actual-version";
		public const string StreamName = "stream-name";
		public const string GroupName = "group-name";
		public const string Reason = "reason";
		public const string MaximumAppendSize = "maximum-append-size";
		public const string MaximumAppendEventSize = "maximum-append-event-size";
		public const string ProposedAppendEventSize = "proposed-append-event-size";
		public const string RequiredMetadataProperties = "required-metadata-properties";
		public const string ScavengeId = "scavenge-id";
		public const string LeaderEndpointHost = "leader-endpoint-host";
		public const string LeaderEndpointPort = "leader-endpoint-port";
		public const string EventId = "event-id";

		public const string LoginName = "login-name";
	}

	public static class Metadata {
		public const string Type = "type";
		public const string Created = "created";
		public const string ContentType = "content-type";
		public const string MetadataContentType = "metadata-content-type";
		public static readonly string[] RequiredMetadata = { Type, ContentType };

		public static class ContentTypes {
			public const string ApplicationJson = "application/json";
			public const string ApplicationOctetStream = "application/octet-stream";
		}

        public const string SchemaVersionId = "schema-version-id";
        public const string MetadataSchemaVersionId = "metadata-schema-version-id";
	}

	public static class Headers {
		public const string Authorization = "authorization";
		public const string BasicScheme = "Basic";

		public const string ConnectionName = "connection-name";
		public const string RequiresLeader = "requires-leader";
	}
}
