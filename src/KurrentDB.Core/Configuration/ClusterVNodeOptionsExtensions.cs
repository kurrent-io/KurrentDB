// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using KurrentDB.Common.Exceptions;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Certificates;
using Serilog;

namespace KurrentDB.Core;

public static class ClusterVNodeOptionsExtensions {
	public static ClusterVNodeOptions Reload(this ClusterVNodeOptions options) =>
		options.ConfigurationRoot == null
			? options
			: ClusterVNodeOptions.FromConfiguration(options.ConfigurationRoot);

	public static ClusterVNodeOptions WithPlugableComponents(this ClusterVNodeOptions options, ISubsystemsPlugin subsystemsPlugin) =>
		options with { PlugableComponents = [.. options.PlugableComponents, .. subsystemsPlugin.GetSubsystems()] };

	public static ClusterVNodeOptions WithPlugableComponent(this ClusterVNodeOptions options, IPlugableComponent plugableComponent) =>
		options with { PlugableComponents = [.. options.PlugableComponents, plugableComponent] };

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

		if (!TryLoadCertificate(
			logLabel: "node",
			store: new StoreCertInfo(
				StoreLocation: options.CertificateStore.CertificateStoreLocation,
				StoreName: options.CertificateStore.CertificateStoreName,
				SubjectName: options.CertificateStore.CertificateSubjectName,
				Thumbprint: options.CertificateStore.CertificateThumbprint),
			file: new FileCertInfo(
				File: options.CertificateFile.CertificateFile,
				PrivateKeyFile: options.CertificateFile.CertificatePrivateKeyFile,
				Password: options.CertificateFile.CertificatePassword,
				PrivateKeyPassword: options.CertificateFile.CertificatePrivateKeyPassword),
			certificate: out var certificate,
			intermediates: out var intermediates)) {

			throw new InvalidConfigurationException(
				"A certificate is required unless insecure mode (--insecure) is set.");
		}

		return (certificate, intermediates);
	}

	private static bool TryLoadCertificate(
		string logLabel,
		StoreCertInfo store,
		FileCertInfo file,
		out X509Certificate2 certificate,
		out X509Certificate2Collection intermediates) {

		if (!string.IsNullOrWhiteSpace(store.StoreLocation)) {
			var location = CertificateUtils.GetCertificateStoreLocation(store.StoreLocation);
			var name = CertificateUtils.GetCertificateStoreName(store.StoreName);
			certificate = CertificateUtils.LoadFromStore(
				location,
				name,
				store.SubjectName,
				store.Thumbprint);
			intermediates = null;
			return true;
		}

		if (!string.IsNullOrWhiteSpace(store.StoreName)) {
			var name = CertificateUtils.GetCertificateStoreName(store.StoreName);
			certificate = CertificateUtils.LoadFromStore(name, store.SubjectName, store.Thumbprint);
			intermediates = null;
			return true;
		}

		if (file.File.IsNotEmptyString()) {
			Log.Information("Loading the {label} certificate(s) from file: {path}", logLabel, file.File);
			(certificate, intermediates) = CertificateUtils.LoadFromFile(
				file.File,
				file.PrivateKeyFile,
				file.Password,
				file.PrivateKeyPassword);
			return true;
		}

		certificate = null;
		intermediates = null;
		return false;
	}

	private record StoreCertInfo(string StoreLocation, string StoreName, string SubjectName, string Thumbprint);
	private record FileCertInfo(string File, string PrivateKeyFile, string Password, string PrivateKeyPassword);

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
		if (options.TrustedRootCertificates != null)
			//used by test code paths only
			return options.TrustedRootCertificates;

		return LoadTrustedRootsFromStoreOrPath(
			store: new StoreCertInfo(
				StoreLocation: options.CertificateStore.TrustedRootCertificateStoreLocation,
				StoreName: options.CertificateStore.TrustedRootCertificateStoreName,
				SubjectName: options.CertificateStore.TrustedRootCertificateSubjectName,
				Thumbprint: options.CertificateStore.TrustedRootCertificateThumbprint),
			path: options.Certificate.TrustedRootCertificatesPath);
	}

	private static X509Certificate2Collection LoadTrustedRootsFromStoreOrPath(StoreCertInfo store, string path) {
		var trustedRootCerts = new X509Certificate2Collection();

		if (!string.IsNullOrWhiteSpace(store.StoreLocation)) {
			var location = CertificateUtils.GetCertificateStoreLocation(store.StoreLocation);
			var name = CertificateUtils.GetCertificateStoreName(store.StoreName);
			trustedRootCerts.Add(CertificateUtils.LoadFromStore(location, name, store.SubjectName, store.Thumbprint));
			return trustedRootCerts;
		}

		if (!string.IsNullOrWhiteSpace(store.StoreName)) {
			var name = CertificateUtils.GetCertificateStoreName(store.StoreName);
			trustedRootCerts.Add(CertificateUtils.LoadFromStore(name, store.SubjectName, store.Thumbprint));
			return trustedRootCerts;
		}

		if (string.IsNullOrEmpty(path)) {
			throw new InvalidConfigurationException(
				$"{nameof(ClusterVNodeOptions.CertificateOptions.TrustedRootCertificatesPath)} must be specified unless insecure mode (--insecure) is set.");
		}

		Log.Information("Loading trusted root certificates from path: {path}", path);
		foreach (var (fileName, cert) in CertificateUtils.LoadAllCertificates(path)) {
			trustedRootCerts.Add(cert);
			Log.Information("Loading trusted root certificate file: {file}", fileName);
		}

		if (trustedRootCerts.Count == 0)
			throw new InvalidConfigurationException(
				$"No trusted root certificate files were loaded from the specified path: {path}");
		return trustedRootCerts;
	}
}
