// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Testing;

public record NodeShimOptions {
	public bool Insecure { get; set; } = true;
	public string Username { get; set; } = "admin";
	public string Password { get; set; } = "changeit";
	public NodeType NodeType { get; set; } = NodeType.Embedded;
	public ContainerOptions Container { get; set; } = new();
	public EmbeddedOptions Embedded { get; set; } = new();
	public ExternalOptions External { get; set; } = new();

	public record ContainerOptions {
		public bool CleanUp { get; set; } = true;
		public string Registry { get; set; } = "docker.kurrent.io/kurrent-preview";
		public string Repository { get; set; } = "kurrentdb";
		public string Tag { get; set; } = "nightly";
	}

	public record EmbeddedOptions {
		// Todo: rather not have to list these explicitly, maybe we can pass through everything
		public string TrustedRootCertificatesPath { get; set; } = "";
		public string CertificateFile { get; set; } = "";
		public string CertificatePrivateKeyFile { get; set; } = "";
	}

	public record ExternalOptions {
		public string Host { get; set; } = "localhost";
		public int Port { get; set; } = 2113;
	}
}
