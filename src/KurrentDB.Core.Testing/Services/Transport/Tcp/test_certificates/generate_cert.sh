#!/bin/bash

if [ "$#" -ne 1 ]; then
    echo "generate_cert.sh <certificate_name>"
    echo "generate_cert.sh node1"
    exit
fi

certificate_name=$1

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
pushd DIR &>/dev/null

echo "Creating certificate directory: $certificate_name"
mkdir "$certificate_name"
pushd "$certificate_name" &>/dev/null

echo "Generating key: $certificate_name.key"
openssl genrsa -out "$certificate_name".key 2048 &>/dev/null

echo "Generating CSR: $certificate_name.csr"
openssl req -new -sha256 -key "$certificate_name".key -subj "/CN=eventstoredb-node" -out "$certificate_name".csr  &>/dev/null

echo "Generating Certificate: $certificate_name.crt"
openssl x509 \
    -req \
    -sha256 \
    -days 10000 \
    -in "$certificate_name".csr \
    -CA ../ca/ca.crt -CAkey ../ca/ca.key \
    -passin pass:password \
    -CAcreateserial \
    -extfile <(\
    printf "
        authorityKeyIdentifier=keyid,issuer
        basicConstraints=CA:FALSE
        keyUsage = digitalSignature, nonRepudiation, keyEncipherment
        extendedKeyUsage = serverAuth, clientAuth
        subjectAltName = IP:127.0.0.1,DNS:localhost"
    ) \
    -out "$certificate_name".crt &>/dev/null

echo "Deleting CSR file"
rm "$certificate_name".csr &>/dev/null
rm ../ca/ca.srl &>/dev/null

popd &>/dev/null

popd &>/dev/null

echo "Done!"
