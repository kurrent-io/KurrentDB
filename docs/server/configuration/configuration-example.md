---
title: "Configutation Example"
order: 3
---
You must define four fundamental aspects to set up a basic configuration for your cluster.

## Data Storage Configuration
First, define the directory where each node database, index, and log file is stored.
```bash
Db: C:\Path\To\Folder\Cluster\node1\Data
Index: C:\Path\To\Folder\Cluster\node1\Index
Log: C:\Path\To\Folder\Cluster\node1\Log
```

## Network Configuration
After defining the storage directory, configure each node's network settings.
```bash
ReplicationIp: 127.0.0.1
NodeIp: 127.0.0.1
NodePort: 21131
ReplicationPort: 11121
EnableAtomPubOverHTTP: true
```

## Cluster Configuration
After configuring each node's network settings, define the cluster settings to specify a cluster size and set up gossip seeds for node discovery.
```bash
ClusterSize: 3
DiscoverViaDns: false
GossipSeed: 127.0.0.1:21132,127.0.0.1:21133
```

## Certificates Configuration
To secure the cluster, you should configure the certificates for secure communication. It specifies the paths to the node's certificate file, private key file, and trusted root certificates.
```bash
CertificateFile: C:\Path\To\Folder\Cluster\node\node.crt
CertificatePrivateKeyFile: C:\Path\To\Folder\Cluster\node\certificates\node.key
TrustedRootCertificatesPath: C:\Path\To\Folder\es-gencert-cli_1.3.0_Windows-x86_64\ca
```
