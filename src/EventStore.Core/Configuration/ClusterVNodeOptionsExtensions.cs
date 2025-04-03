// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Exceptions;
using EventStore.Common.Utils;
using EventStore.Plugins;
using Serilog;

namespace EventStore.Core;

public static class ClusterVNodeOptionsExtensions {
	public static ClusterVNodeOptions Reload(this ClusterVNodeOptions options) =>
		options.ConfigurationRoot == null
			? options
			: ClusterVNodeOptions.FromConfiguration(options.ConfigurationRoot);

	public static ClusterVNodeOptions WithPlugableComponent(this ClusterVNodeOptions options, IPlugableComponent plugableComponent) =>
		options with { PlugableComponents = [..options.PlugableComponents, plugableComponent] };

	public static ClusterVNodeOptions InCluster(this ClusterVNodeOptions options, int clusterSize) => options with {
		Cluster = options.Cluster with {
			ClusterSize = clusterSize <= 1
				? throw new ArgumentOutOfRangeException(nameof(clusterSize), clusterSize,
					$"{nameof(clusterSize)} must be greater than 1.")
				: clusterSize
		}
	};

	/// <summary>
	/// Returns a builder set to run in memory only
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions RunInMemory(this ClusterVNodeOptions options) => options with {
		Database = options.Database with {
			MemDb = true,
			Db = new ClusterVNodeOptions().Database.Db
		}
	};

	/// <summary>
	/// Returns a builder set to write database files to the specified path
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="path">The path on disk in which to write the database files</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions RunOnDisk(this ClusterVNodeOptions options, string path) => options with {
		Database = options.Database with {
			MemDb = false,
			Db = path
		}
	};

	/// <summary>
	/// Runs the node in insecure mode.
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions Insecure(this ClusterVNodeOptions options) => options with {
		Application = options.Application with {
			Insecure = true
		},
		ServerCertificate = null,
		TrustedRootCertificates = null
	};

	/// <summary>
	/// Runs the node in secure mode.
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="trustedRootCertificates">A <see cref="X509Certificate2Collection"/> containing trusted root <see cref="X509Certificate2"/></param>
	/// <param name="serverCertificate">A <see cref="X509Certificate2"/> for the server</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions Secure(this ClusterVNodeOptions options,
		X509Certificate2Collection trustedRootCertificates, X509Certificate2 serverCertificate) => options with {
		Application = options.Application with {
			Insecure = false,
		},
		ServerCertificate = serverCertificate,
		TrustedRootCertificates = trustedRootCertificates
	};

	/// <summary>
	/// Sets gossip seeds to the specified value and turns off dns discovery
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="gossipSeeds">The list of gossip seeds</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions WithGossipSeeds(this ClusterVNodeOptions options, EndPoint[] gossipSeeds) =>
		options with {
			Cluster = options.Cluster with {
				GossipSeed = gossipSeeds,
				DiscoverViaDns = false,
				ClusterDns = string.Empty
			}
		};

	/// <summary>
	/// Sets the external tcp endpoint to the specified value
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="endPoint">The external endpoint to use</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions WithExternalTcpOn(
		this ClusterVNodeOptions options, IPEndPoint endPoint) =>
		options with {
			Interface = options.Interface with {
				NodeIp = endPoint.Address,
			}
		};

	/// <summary>
	/// Sets the internal tcp endpoint to the specified value
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="endPoint">The internal endpoint to use</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions WithReplicationEndpointOn(
		this ClusterVNodeOptions options, IPEndPoint endPoint) =>
		options with {
			Interface = options.Interface with {
				ReplicationIp = endPoint.Address,
				ReplicationPort = endPoint.Port,
			}
		};

	/// <summary>
	/// Sets the http endpoint to the specified value
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="endPoint">The http endpoint to use</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions WithNodeEndpointOn(
		this ClusterVNodeOptions options, IPEndPoint endPoint) =>
		options with {
			Interface = options.Interface with {
				NodeIp = endPoint.Address,
				NodePort = endPoint.Port
			}
		};

	/// <summary>
	/// Sets up the External Host that would be advertised
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="endPoint">The advertised host</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions
		AdvertiseExternalHostAs(this ClusterVNodeOptions options, EndPoint endPoint) =>
		options with {
			Interface = options.Interface with {
				NodeHostAdvertiseAs = endPoint.GetHost(),
			}
		};

	/// <summary>
	/// Sets up the Internal Host that would be advertised
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="endPoint">The advertised host</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions
		AdvertiseInternalHostAs(this ClusterVNodeOptions options, EndPoint endPoint) =>
		options with {
			Interface = options.Interface with {
				ReplicationHostAdvertiseAs = endPoint.GetHost(),
				ReplicationTcpPortAdvertiseAs = endPoint.GetPort()
			}
		};

	/// <summary>
	/// </summary>
	/// <param name="options">The <see cref="ClusterVNodeOptions"/></param>
	/// <param name="endPoint">The advertised host</param>
	/// <returns>A <see cref="ClusterVNodeOptions"/> with the options set</returns>
	public static ClusterVNodeOptions AdvertiseNodeAs(this ClusterVNodeOptions options, EndPoint endPoint) =>
		options with {
			Interface = options.Interface with {
				NodeHostAdvertiseAs = endPoint.GetHost(),
				NodePortAdvertiseAs = endPoint.GetPort()
			}
		};

	/// <summary>
	/// </summary>
	/// <param name="options"></param>
	/// <returns></returns>
	/// <exception cref="InvalidConfigurationException"></exception>
	public static (X509Certificate2 certificate, X509Certificate2Collection intermediates) LoadNodeCertificate(
		this ClusterVNodeOptions options) {
		if (options.ServerCertificate != null) {
			//used by test code paths only
			return (options.ServerCertificate!, null);
		}


		if (!string.IsNullOrWhiteSpace(options.CertificateStore.CertificateStoreLocation)) {
			var location =
				CertificateUtils.GetCertificateStoreLocation(options.CertificateStore.CertificateStoreLocation);
			var name = CertificateUtils.GetCertificateStoreName(options.CertificateStore.CertificateStoreName);
			return (CertificateUtils.LoadFromStore(location, name, options.CertificateStore.CertificateSubjectName,
				options.CertificateStore.CertificateThumbprint), null);
		}

		if (!string.IsNullOrWhiteSpace(options.CertificateStore.CertificateStoreName)) {
			var name = CertificateUtils.GetCertificateStoreName(options.CertificateStore.CertificateStoreName);
			return (
				CertificateUtils.LoadFromStore(name, options.CertificateStore.CertificateSubjectName,
					options.CertificateStore.CertificateThumbprint), null);
		}

		if (options.CertificateFile.CertificateFile.IsNotEmptyString()) {
			Log.Information("Loading the node's certificate(s) from file: {path}",
				options.CertificateFile.CertificateFile);
			return CertificateUtils.LoadFromFile(options.CertificateFile.CertificateFile,
				options.CertificateFile.CertificatePrivateKeyFile, options.CertificateFile.CertificatePassword,
				options.CertificateFile.CertificatePrivateKeyPassword);
		}

		throw new InvalidConfigurationException(
			"A certificate is required unless insecure mode (--insecure) is set.");
	}

	/// <summary>
	/// Loads an <see cref="X509Certificate2Collection"/> from the options set.
	/// If either TrustedRootCertificateStoreLocation or TrustedRootCertificateStoreName is set,
	/// then the certificates will only be loaded from the certificate store.
	/// Otherwise, the certificates will be loaded from the path specified by TrustedRootCertificatesPath.
	/// </summary>
	/// <param name="options"></param>
	/// <returns></returns>
	/// <exception cref="InvalidConfigurationException"></exception>
	public static X509Certificate2Collection LoadTrustedRootCertificates(this ClusterVNodeOptions options) {
		if (options.TrustedRootCertificates != null) return options.TrustedRootCertificates;
		var trustedRootCerts = new X509Certificate2Collection();

		if (!string.IsNullOrWhiteSpace(options.CertificateStore.TrustedRootCertificateStoreLocation)) {
			var location =
				CertificateUtils.GetCertificateStoreLocation(options.CertificateStore
					.TrustedRootCertificateStoreLocation);
			var name = CertificateUtils.GetCertificateStoreName(options.CertificateStore
				.TrustedRootCertificateStoreName);
			trustedRootCerts.Add(CertificateUtils.LoadFromStore(location, name,
				options.CertificateStore.TrustedRootCertificateSubjectName,
				options.CertificateStore.TrustedRootCertificateThumbprint));
			return trustedRootCerts;
		}

		if (!string.IsNullOrWhiteSpace(options.CertificateStore.TrustedRootCertificateStoreName)) {
			var name = CertificateUtils.GetCertificateStoreName(options.CertificateStore
				.TrustedRootCertificateStoreName);
			trustedRootCerts.Add(CertificateUtils.LoadFromStore(name,
				options.CertificateStore.TrustedRootCertificateSubjectName,
				options.CertificateStore.TrustedRootCertificateThumbprint));
			return trustedRootCerts;
		}

		if (string.IsNullOrEmpty(options.Certificate.TrustedRootCertificatesPath)) {
			throw new InvalidConfigurationException(
				$"{nameof(options.Certificate.TrustedRootCertificatesPath)} must be specified unless insecure mode (--insecure) is set.");
		}

		Log.Information("Loading trusted root certificates.");
		foreach (var (fileName, cert) in CertificateUtils
			         .LoadAllCertificates(options.Certificate.TrustedRootCertificatesPath)) {
			trustedRootCerts.Add(cert);
			Log.Information("Loading trusted root certificate file: {file}", fileName);
		}

		if (trustedRootCerts.Count == 0)
			throw new InvalidConfigurationException(
				$"No trusted root certificate files were loaded from the specified path: {options.Certificate.TrustedRootCertificatesPath}");
		return trustedRootCerts;
	}
}
