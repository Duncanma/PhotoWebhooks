#!/usr/bin/env bash
set -euo pipefail

APP_NAME="ProcessPhotoPurchases"

if ! command -v func >/dev/null 2>&1; then
  echo "Error: Azure Functions Core Tools ('func') is not installed or not in PATH." >&2
  exit 1
fi

echo "Deploying ${APP_NAME}..."
func azure functionapp publish "${APP_NAME}" --dotnet-isolated
