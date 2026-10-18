#!/usr/bin/env sh
# Creates a throwaway CA and a TLS certificate for the bridge under test (valid for localhost,
# 127.0.0.1 and the compose service names). Test use only; the output folder is ignored by git.
set -eu

cd "$(dirname "$0")"
out=out
mkdir -p "$out"

# Python 3.13 verifies strictly (VERIFY_X509_STRICT): the CA needs key usage, the leaf key identifiers.
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=Donetick CalDAV e2e CA" \
  -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -addext "subjectKeyIdentifier=hash" \
  -keyout "$out/ca.key" -out "$out/ca.crt" 2>/dev/null

openssl req -newkey rsa:2048 -nodes -subj "/CN=localhost" \
  -keyout "$out/bridge.key" -out "$out/bridge.csr" 2>/dev/null

cat > "$out/san.ext" <<'EXT'
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,IP:127.0.0.1,DNS:bridge,DNS:bridge-labels,DNS:bridge-contract
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
EXT

openssl x509 -req -in "$out/bridge.csr" -CA "$out/ca.crt" -CAkey "$out/ca.key" -CAcreateserial \
  -days 30 -extfile "$out/san.ext" -out "$out/bridge.crt" 2>/dev/null

# Readable for a non-root container user as well (finding F-18 proposes USER app). Test key only.
chmod 644 "$out/bridge.key"
rm -f "$out/bridge.csr" "$out/san.ext" "$out/ca.srl"
echo "Certificates written to $(pwd)/$out (trust ca.crt in the test client)"
