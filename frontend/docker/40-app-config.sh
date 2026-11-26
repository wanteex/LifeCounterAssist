#!/bin/sh
# Writes the runtime configuration consumed by src/config.ts from environment variables.
set -eu

json_string() {
  printf '"%s"' "$(printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g')"
}

cat > /usr/share/nginx/html/config.js <<EOF
window.__APP_CONFIG__ = {
  apiUrl: $(json_string "${API_URL:-http://localhost:5075}"),
  authMode: $(json_string "${AUTH_MODE:-entra}"),
  clientId: $(json_string "${ENTRA_CLIENT_ID:-}"),
  authority: $(json_string "${ENTRA_AUTHORITY:-}"),
  apiScope: $(json_string "${ENTRA_API_SCOPE:-}"),
}
EOF
