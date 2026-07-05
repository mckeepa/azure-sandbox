#!/usr/bin/env bash
set -euo pipefail

# Rotate the on-premises client certificate used by the sample app.
# This script creates a new key pair, stores it in a dedicated folder for the app,
# archives the previous files, and uploads the public certificate to the app registration.
#
# Usage:
#   APP_ID=<app-registration-id> TENANT_ID=<tenant-id> ./scripts/rotate-client-certificate.sh
#
# The script expects Azure CLI to be installed and authenticated when APP_ID is provided.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
CERT_DIR="$PROJECT_DIR/certs"
ARCHIVE_DIR="$CERT_DIR/archive/$(date +%Y%m%d-%H%M%S)"

APP_ID="${APP_ID:-}"
TENANT_ID="${TENANT_ID:-}"
APP_NAME="${APP_NAME:-OnPrem-CSharp-WebApp}"
DISPLAY_NAME="${DISPLAY_NAME:-$APP_NAME-$(date +%Y%m%d%H%M%S)}"
DAYS="${DAYS:-365}"

mkdir -p "$CERT_DIR" "$ARCHIVE_DIR"

PRIVATE_KEY_PATH="$CERT_DIR/private.pem"
PUBLIC_CERT_PATH="$CERT_DIR/public.crt"

if [[ -f "$PRIVATE_KEY_PATH" ]]; then
  cp "$PRIVATE_KEY_PATH" "$ARCHIVE_DIR/private.pem"
fi

if [[ -f "$PUBLIC_CERT_PATH" ]]; then
  cp "$PUBLIC_CERT_PATH" "$ARCHIVE_DIR/public.crt"
fi

openssl req -x509 -newkey rsa:2048 -nodes \
  -days "$DAYS" \
  -subj "/CN=$APP_NAME" \
  -keyout "$PRIVATE_KEY_PATH" \
  -out "$PUBLIC_CERT_PATH"

if [[ -n "$APP_ID" ]]; then
  if ! command -v az >/dev/null 2>&1; then
    echo "Azure CLI not found. Public certificate was generated locally but was not uploaded to Azure." >&2
    exit 1
  fi

  echo "Uploading public certificate to app registration $APP_ID"
  az ad app credential reset \
    --id "$APP_ID" \
    --append \
    --display-name "$DISPLAY_NAME" \
    --cert "@$PUBLIC_CERT_PATH" \
    --output none
fi

echo "Certificate rotation complete."
echo "Private key: $PRIVATE_KEY_PATH"
echo "Public certificate: $PUBLIC_CERT_PATH"
echo "Archive: $ARCHIVE_DIR"
