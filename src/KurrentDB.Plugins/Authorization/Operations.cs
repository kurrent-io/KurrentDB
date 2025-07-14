// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Authorization;

public static class Operations {
	public static class Node {
		const string Resource = "node";
		public static readonly OperationDefinition Redirect = new Operation(Resource, "redirect");
		public static readonly OperationDefinition Options = new Operation(Resource, "options");
		public static readonly OperationDefinition Ping = new Operation(Resource, "ping");

		public static readonly OperationDefinition StaticContent = new($"{Resource}/content", "read");

		public static readonly OperationDefinition Shutdown = new(Resource, "shutdown");
		public static readonly OperationDefinition ReloadConfiguration = new(Resource, "reloadConfiguration");
		public static readonly OperationDefinition MergeIndexes = new(Resource, "mergeIndexes");
		public static readonly OperationDefinition SetPriority = new(Resource, "setPriority");
		public static readonly OperationDefinition Resign = new(Resource, "resign");

		public static readonly OperationDefinition Login = new Operation(Resource, "login");

		public static class Scavenge {
			const string Resource = $"{Node.Resource}/scavenge";
			public static readonly OperationDefinition Start = new(Resource, "start");
			public static readonly OperationDefinition Stop = new(Resource, "stop");
			public static readonly OperationDefinition Read = new(Resource, "read");
		}

		public static class Redaction {
			const string Resource = $"{Node.Resource}/redaction";
			public static readonly OperationDefinition SwitchChunk = new(Resource, "switchChunk");
		}

		public static class Information {
			const string Resource = $"{Node.Resource}/info";

			public static readonly OperationDefinition Subsystems = new($"{Resource}/subsystems", "read");

			public static readonly OperationDefinition Histogram = new($"{Resource}/histograms", "read");

			public static readonly OperationDefinition Read = new(Resource, "read");

			public static readonly OperationDefinition Options = new($"{Resource}/options", "read");
		}

		public static class Statistics {
			const string Resource = $"{Node.Resource}/stats";
			public static readonly OperationDefinition Read = new(Resource, "read");

			public static readonly OperationDefinition Replication = new($"{Resource}/replication", "read");

			public static readonly OperationDefinition Tcp = new($"{Resource}/tcp", "read");

			public static readonly OperationDefinition Custom = new($"{Resource}/custom", "read");
		}

		public static class Elections {
			const string Resource = $"{Node.Resource}/elections";
			public static readonly OperationDefinition ViewChange = new(Resource, "viewchange");

			public static readonly OperationDefinition ViewChangeProof = new(Resource, "viewchangeproof");

			public static readonly OperationDefinition Prepare = new(Resource, "prepare");
			public static readonly OperationDefinition PrepareOk = new(Resource, "prepareOk");
			public static readonly OperationDefinition Proposal = new(Resource, "proposal");
			public static readonly OperationDefinition Accept = new(Resource, "accept");

			public static readonly OperationDefinition LeaderIsResigning = new(Resource, "leaderisresigning");

			public static readonly OperationDefinition LeaderIsResigningOk = new(Resource, "leaderisresigningok");
		}

		public static class Gossip {
			const string Resource = $"{Node.Resource}/gossip";
			public static readonly OperationDefinition Read = new(Resource, "read");
			public static readonly OperationDefinition Update = new(Resource, "update");
			public static readonly OperationDefinition ClientRead = new($"{Resource}/client", "read");
		}

		public static class Transform {
			const string Resource = "transform";
			public static readonly OperationDefinition Set = new(Resource, "set");
		}
	}

	public static class Streams {
		const string Resource = "streams";
		public static readonly OperationDefinition Read = new(Resource, "read");
		public static readonly OperationDefinition Write = new(Resource, "write");
		public static readonly OperationDefinition Delete = new(Resource, "delete");
		public static readonly OperationDefinition MetadataRead = new(Resource, "metadataRead");

		public static readonly OperationDefinition MetadataWrite = new(Resource, "metadataWrite");

		public static class Parameters {
			public static Parameter StreamId(string streamId) => new("streamId", streamId);

			public static Parameter TransactionId(long transactionId) => new("transactionId", transactionId.ToString("D"));
		}
	}

	public static class Subscriptions {
		const string Resource = "subscriptions";
		public static readonly OperationDefinition Statistics = new(Resource, "statistics");
		public static readonly OperationDefinition Create = new(Resource, "create");
		public static readonly OperationDefinition Update = new(Resource, "update");
		public static readonly OperationDefinition Delete = new(Resource, "delete");
		public static readonly OperationDefinition ReplayParked = new(Resource, "replay");
		public static readonly OperationDefinition Restart = new(Resource, "restart");

		public static readonly OperationDefinition ProcessMessages = new(Resource, "process");


		public static class Parameters {
			public static Parameter SubscriptionId(string id) => new("subscriptionId", id);

			public static Parameter StreamId(string streamId) => new("streamId", streamId);
		}
	}

	public static class Users {
		const string Resource = "users";
		public static readonly OperationDefinition Create = new(Resource, "create");
		public static readonly OperationDefinition Update = new(Resource, "update");
		public static readonly OperationDefinition Delete = new(Resource, "delete");
		public static readonly OperationDefinition List = new(Resource, "list");
		public static readonly OperationDefinition Read = new(Resource, "read");
		public static readonly OperationDefinition CurrentUser = new(Resource, "self");
		public static readonly OperationDefinition Enable = new(Resource, "enable");
		public static readonly OperationDefinition Disable = new(Resource, "disable");

		public static readonly OperationDefinition ResetPassword = new(Resource, "resetPassword");

		public static readonly OperationDefinition ChangePassword = new(Resource, "updatePassword");

		public static class Parameters {
			public const string UserParameterName = "user";

			public static Parameter User(string userId) => new(UserParameterName, userId);
		}
	}

	public static class Projections {
		const string Resource = "projections";

		public static readonly OperationDefinition Create = new(Resource, "create");
		public static readonly OperationDefinition Update = new(Resource, "update");
		public static readonly OperationDefinition Read = new(Resource, "read");

		public static readonly OperationDefinition Abort = new(Resource, "abort");

		public static readonly OperationDefinition List = new(Resource, "list");
		public static readonly OperationDefinition Restart = new(Resource, "restart");
		public static readonly OperationDefinition Delete = new(Resource, "delete");
		public static readonly OperationDefinition Enable = new(Resource, "enable");
		public static readonly OperationDefinition Disable = new(Resource, "disable");
		public static readonly OperationDefinition Reset = new(Resource, "reset");

		public static readonly OperationDefinition ReadConfiguration = new($"{Resource}/configuration", "read");

		public static readonly OperationDefinition UpdateConfiguration = new($"{Resource}/configuration", "update");

		public static readonly OperationDefinition Status = new(Resource, "status");

		//can be Partition based
		public static readonly OperationDefinition State = new(Resource, "state");
		public static readonly OperationDefinition Result = new(Resource, "state");

		public static readonly OperationDefinition Statistics = new(Resource, "statistics");

		//This one is a bit weird
		public static readonly OperationDefinition DebugProjection = new(Resource, "debug");

		public static class Parameters {
			public static readonly Parameter Query = new("projectionType", "query");
			public static readonly Parameter OneTime = new("projectionType", "onetime");
			public static readonly Parameter Continuous = new("projectionType", "continuous");

			public static Parameter Projection(string name) => new("projection", name);

			public static Parameter RunAs(string name) => new("runas", name);
		}
	}
}
