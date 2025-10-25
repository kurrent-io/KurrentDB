openssl genrsa -out ca.key 4096
openssl req -x509 -sha256 -new -nodes -key ca.key -days 10000 -out ca.crt
