#!/usr/bin/env bash
# Regenerates conf/nodeN.conf for a cluster of $1 nodes.
#
# ClusterSize and the seed lists have to agree with how many nodes are actually started, so run.sh
# calls this on every run rather than leaving the files to drift out of step with CLUSTER_SIZE.
set -euo pipefail
cd "$(dirname "$0")"

COUNT="${1:?usage: gen-conf.sh <node-count>}"
mkdir -p conf
rm -f conf/node*.conf

for n in $(seq 1 "$COUNT"); do
	gossip=""; bootstrap=""
	for m in $(seq 1 "$COUNT"); do
		[ "$m" = "$n" ] && continue
		gossip="${gossip:+$gossip,}\"node$m.eventstore:2113\""
		bootstrap="${bootstrap:+$bootstrap,}\"node$m.eventstore:3113\""
	done

	cat > "conf/node$n.conf" <<EOF
{
   "AllowAnonymousEndpointAccess": true,
   "AllowAnonymousStreamAccess": false,
   "AllowUnknownOptions": false,
   "CertificateFile": "/etc/kurrentdb/certs/node$n/node.crt",
   "CertificatePrivateKeyFile": "/etc/kurrentdb/certs/node$n/node.key",
   "TrustedRootCertificatesPath": "/etc/kurrentdb/certs/ca",
   "DisableClientAuthEkuValidation": true,
   "ClusterSecret": "i-enjoy-the-peace-and-quiet-of-network-partitions",
   "ClusterSize": $COUNT,
   "Connectors": {
      "Enabled": false
   },
   "SecondaryIndexing": {
      "Enabled": false
   },
   "Db": "/var/lib/kurrentdb/db",
   "Index": "/var/lib/kurrentdb/index",
   "Log": "/var/log/kurrentdb",
   "DiscoverViaDns": false,
   "EnableAtomPubOverHttp": true,
   "GossipSeed": [$gossip],
   "Insecure": false,
   "DisableTls": false,
   "IsDataPlaneNode": true,
   "IsKontrolPlaneNode": true,
   "KontrolPlaneApiSeed": [$gossip],
   "KontrolPlaneBootstrapSeed": [$bootstrap],
   "KontrolPlaneAppointmentTimeoutMs": 1000,
   "KontrolPlaneLowerElectionTimeoutMs": 700,
   "KontrolPlaneUpperElectionTimeoutMs": 1000,
   "KontrollerHostAdvertiseAs": "node$n.eventstore",
   "KontrollerPort": 3113,
   "LogFailedAuthenticationAttempts": true,
   "NodeHostAdvertiseAs": "node$n.eventstore",
   "NodeIp": "0.0.0.0",
   "NodePort": 2113,
   "NodePortAdvertiseAs": 2113,
   "ReadOnlyReplica": false,
   "ReplicationHostAdvertiseAs": "node$n.eventstore",
   "ReplicationIp": "0.0.0.0",
   "ReplicationPort": 1112,
   "RunProjections": "None",
   "SkipDbVerify": true,
   "SkipIndexVerify": true
}
EOF
done

echo "generated conf for $COUNT nodes"
