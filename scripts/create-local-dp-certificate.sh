#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
mkdir -p .local
chmod 700 .local
if [[ -f .local/data-protection.pfx ]]; then
    echo "Keeping existing local Data Protection certificate."
    exit 0
fi

umask 077
temp_dir=$(mktemp -d)
trap 'rm -f "$temp_dir/key.pem" "$temp_dir/cert.pem"; rmdir "$temp_dir"' EXIT
openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
    -subj "/CN=MentorPath local Data Protection only" \
    -keyout "$temp_dir/key.pem" -out "$temp_dir/cert.pem"
openssl pkcs12 -export -inkey "$temp_dir/key.pem" -in "$temp_dir/cert.pem" \
    -out .local/data-protection.pfx -passout pass:
# Compose mounts this file read-only for the non-root app; the host directory stays private.
chmod 644 .local/data-protection.pfx
echo "Created local-only Data Protection certificate. Never use it in a real deployment."
