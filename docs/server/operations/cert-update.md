---
order: 5
title: Renew certificates
---

# Certificate update upon expiry

In KurrentDB, the certificates require updating when they have expired or are going to expire soon. Follow the steps below to perform a rolling certificate update.

## Step 1: Generate new certificates

The new certificates can be created in the same manner as you generated the existing certificates.
You can use the [es-gencert-cli](https://github.com/EventStore/es-gencert-cli) tool to generate the CA and node certificates.
You can also follow the [Configurator](https://configurator.eventstore.com/) to create commands for generating the certificates based on your cluster's configuration.

::: tip
It is possible to do a rolling update with new certificates having a Common Name (CN) that's different from the original certificates. If `CertificateReservedNodeCommonName` was set in your configuration, any changes to its value will be taken into consideration during a config reload.
:::

## Step 2: Replace the certificates

The next step is to replace the outdated certificates with the newly generated certificates.

If you are using symlinks, then you can update the symlink to point it to the new certificates.

### Linux

We normally have a symlink that points to the existing certificates:

ca -> `/etc/kurrentdb/certs/ca`
node.crt -> `/etc/kurrentdb/certs/node.crt`
node.key -> `/etc/kurrentdb/certs/node.key`

Update the symlink so that the link points to the new certificates:

ca -> `/etc/kurrentdb/newCerts/ca`
node.crt -> `/etc/kurrentdb/newCerts/node.crt`
node.key -> `/etc/kurrentdb/newCerts/node.key`

In the above example we have the links in a folder at path `/home/ubuntu/links`.

## Step 3: Reload the configuration

You can reload the certificate configuration without restarting the node by issuing the following curl command.

```bash
curl -k -X POST --basic https://{nodeAddress}:{HttpPort}/admin/reloadconfig -u {username}:{Password}
```

For Example:

```bash
curl -k -X POST --basic https://127.0.0.1:2113/admin/reloadconfig -u admin:changeit
```

### Linux

Linux users can also send the `SIGHUP` signal to KurrentDB to reload the certificates.

For Example:

```bash
kill -s SIGHUP 38956
```

Here `38956` is the Process ID (PID) of KurrentDB on our local machine. To find the PID, use the following command in the Linux terminal.

```bash
pidof kurrentd
```

Once the configuration has been reloaded successfully, the server logs will look something like:

```
[108277,30,14:46:07.453,INF] Reloading the node's configuration since a request has been received on /admin/reloadconfig.
[108277,29,14:46:07.457,INF] Loading the node's certificate(s) from file: "/home/ubuntu/links/node.crt"
[108277,29,14:46:07.488,INF] Loading the node's certificate. Subject: "CN=kurrentdb-node", Previous thumbprint: "05526714107700C519E24794E8964A3B30EF9BD0", New thumbprint: "BE6D5CD681D7B9281D60A5969B5A7E31AF775E9F"
[108277,29,14:46:07.488,INF] Loading intermediate certificate. Subject: "CN=KurrentDB Intermediate CA 78ae8d5a159b247568039cf64f4b04ad, O=Kurrent Inc, C=UK", Thumbprint: "ED4AA0C5ED4AD120DDEE2FA8B3B0CCC5A30B81E3"
[108277,29,14:46:07.489,INF] Loading trusted root certificates.
[108277,29,14:46:07.490,INF] Loading trusted root certificate file: "/home/ubuntu/links/ca/ca.crt"
[108277,29,14:46:07.491,INF] Loading trusted root certificate. Subject: "CN=KurrentDB CA c39fece76b4efcb65846145c942c037c, O=Kurrent Inc, C=UK", Thumbprint: "5F020804E8D7C419F9FC69ED3B4BC72DD79A5996"
[108277,29,14:46:07.493,INF] Certificate chain verification successful.
[108277,29,14:46:07.493,INF] All certificates successfully loaded.
[108277,29,14:46:07.494,INF] The node's configuration was successfully reloaded
```

::: tip
If there are any errors during loading certificates, the new configuration changes will not take effect.
:::

The server logs the following error when the node certificate is not available at the path specified in the KurrentDB configuration file.

```
[108277,30,14:49:47.481,INF] Reloading the node's configuration since a request has been received on /admin/reloadconfig.
[108277,29,14:49:47.488,INF] Loading the node's certificate(s) from file: "/home/ubuntu/links/node.crt"
[108277,29,14:49:47.489,ERR] An error has occurred while reloading the configuration
System.IO.FileNotFoundException: Could not find file '/home/ubuntu/links/node.key'.
```

## Step 4: Update the other nodes

Now update the certificates on the other nodes.

## Step 5: Monitor the cluster

The connection between nodes in KurrentDB reset every 10 minutes so the new configuration won’t take immediate effect. Use this time to update the certificates on all of the nodes. It is advisable to monitor the cluster after this timeframe to make sure everything is working as expected.
The KurrentDB will log certificate errors if the certificates are not reloaded on all of the nodes before the connection resets.

```
[108277,29,14:59:47.489,ERR] kurrentdb-node : A certificate chain could not be built to a trusted root authority.
[108277,29,14:59:47.489,ERR] Client certificate validation error: "The certificate (CN=kurrentdb-node) provided by the client failed validation with the following error(s): RemoteCertificateChainErrors (PartialChain)"
```

The above error indicates that there is some issue with the certificate because the reload process is not yet completed on all the nodes. The error can be safely ignored during the duration of the reloading certificate process, but the affected nodes will not be able to communicate with each other until the certificates have been updated on all the nodes.

::: warning
It can take up to 10 minutes for certificate changes to take effect. Certificates on all nodes should be updated within this window to avoid connection issues.
:::
