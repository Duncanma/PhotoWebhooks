#!/usr/bin/env bash
# Backfill analytics aggregates one calendar day at a time via /api/stats/backfill.
# Skips days/types that already have aggregate data (use --force to override).
#
# Usage:
#   ./scripts/backfill-analytics.sh START_DATE [END_DATE]
#   ./scripts/backfill-analytics.sh --days 30
#
# Dates: YYYY-MM-DD (UTC). END defaults to today (UTC) if omitted.
#
# Requires:
#   ANALYTICS_DASHBOARD_SECRET, or Azure CLI access to read it from the function app.
#
# Optional env:
#   ANALYTICS_HOST   (default: functions.duncanmackenzie.net)
#   ANALYTICS_TYPES  (default: day,path,referrer,country)
#   CURL_TIMEOUT_SEC (default: 600 per day)

set -euo pipefail

RESOURCE_GROUP="${ANALYTICS_RESOURCE_GROUP:-Blog}"
APP_NAME="${ANALYTICS_APP_NAME:-ProcessPhotoPurchases}"
HOST="${ANALYTICS_HOST:-functions.duncanmackenzie.net}"
TYPES="${ANALYTICS_TYPES:-day,path,referrer,country}"
CURL_TIMEOUT_SEC="${CURL_TIMEOUT_SEC:-600}"
FORCE=false

usage() {
  cat <<'EOF'
Backfill analytics aggregates one day at a time.

Usage:
  ./scripts/backfill-analytics.sh START_DATE [END_DATE]
  ./scripts/backfill-analytics.sh --days N [--end YYYY-MM-DD]
  ./scripts/backfill-analytics.sh [--force] ...

Options:
  --force   Backfill even when aggregate data already exists

Examples:
  ./scripts/backfill-analytics.sh 2024-01-01 2026-05-28
  ./scripts/backfill-analytics.sh 2026-05-25
  ./scripts/backfill-analytics.sh --days 7
  ./scripts/backfill-analytics.sh --days 30 --end 2026-05-27
  ./scripts/backfill-analytics.sh --force 2026-05-25 2026-05-28

Environment:
  ANALYTICS_DASHBOARD_SECRET  Secret header value (or fetched via az)
  ANALYTICS_HOST              API host (default: functions.duncanmackenzie.net)
  ANALYTICS_TYPES             Comma-separated types (default: all four)
  CURL_TIMEOUT_SEC            Per-day request timeout (default: 600)
EOF
}

utc_today() {
  date -u +%Y-%m-%d
}

normalize_day() {
  date -u -j -f "%Y-%m-%d" "$1" +%Y-%m-%d 2>/dev/null || date -u -d "$1" +%Y-%m-%d
}

add_one_day() {
  date -u -j -f "%Y-%m-%d" -v+1d "$1" +%Y-%m-%d 2>/dev/null || date -u -d "$1 + 1 day" +%Y-%m-%d
}

resolve_secret() {
  if [[ -n "${ANALYTICS_DASHBOARD_SECRET:-}" ]]; then
    echo "${ANALYTICS_DASHBOARD_SECRET}"
    return
  fi

  if command -v az >/dev/null 2>&1; then
    az functionapp config appsettings list \
      --resource-group "${RESOURCE_GROUP}" \
      --name "${APP_NAME}" \
      --query "[?name=='AnalyticsDashboardSecret'].value | [0]" \
      -o tsv
    return
  fi

  echo "Error: set ANALYTICS_DASHBOARD_SECRET or install Azure CLI (az)." >&2
  exit 1
}

stats_get() {
  local path="$1"
  local secret="$2"
  curl -sS -m 60 \
    -H "X-Analytics-Secret: ${secret}" \
    "https://${HOST}${path}"
}

type_has_data() {
  local day="$1"
  local type="$2"
  local secret="$3"
  local response

  case "${type}" in
    day)
      response="$(stats_get "/api/stats/timeseries?start=${day}&end=${day}" "${secret}")"
      python3 -c 'import json, sys
day = sys.argv[1].replace("-", "")
data = json.loads(sys.argv[2]) if sys.argv[2].strip() else []
print("yes" if any(row.get("period") == day for row in data) else "no")' "${day}" "${response}"
      ;;
    path)
      response="$(stats_get "/api/stats/top-pages?start=${day}&end=${day}&limit=1" "${secret}")"
      python3 -c 'import json, sys
data = json.loads(sys.argv[1]) if sys.argv[1].strip() else []
print("yes" if len(data) > 0 else "no")' "${response}"
      ;;
    referrer)
      response="$(stats_get "/api/stats/referrers?start=${day}&end=${day}&limit=1" "${secret}")"
      python3 -c 'import json, sys
data = json.loads(sys.argv[1]) if sys.argv[1].strip() else []
print("yes" if len(data) > 0 else "no")' "${response}"
      ;;
    country)
      response="$(stats_get "/api/stats/countries?start=${day}&end=${day}&limit=1" "${secret}")"
      python3 -c 'import json, sys
data = json.loads(sys.argv[1]) if sys.argv[1].strip() else []
print("yes" if len(data) > 0 else "no")' "${response}"
      ;;
    *)
      echo "no"
      ;;
  esac
}

missing_types_for_day() {
  local day="$1"
  local secret="$2"
  local missing=()
  local type

  IFS=',' read -r -a type_list <<< "${TYPES}"
  for type in "${type_list[@]}"; do
    type="${type// /}"
    if [[ -z "${type}" ]]; then
      continue
    fi
    if [[ "$(type_has_data "${day}" "${type}" "${secret}")" != "yes" ]]; then
      missing+=("${type}")
    fi
  done

  if [[ ${#missing[@]} -eq 0 ]]; then
    return 1
  fi

  local IFS=','
  echo "${missing[*]}"
}

backfill_day() {
  local day="$1"
  local secret="$2"
  local types="$3"
  local tmp
  tmp="$(mktemp)"

  local http_code
  http_code="$(
    curl -sS -m "${CURL_TIMEOUT_SEC}" \
      -H "X-Analytics-Secret: ${secret}" \
      -o "${tmp}" \
      -w "%{http_code}" \
      "https://${HOST}/api/stats/backfill?start=${day}&end=${day}&types=${types}"
  )"

  if [[ "${http_code}" != "200" ]]; then
    echo "  FAILED HTTP ${http_code}" >&2
    cat "${tmp}" >&2
    rm -f "${tmp}"
    return 1
  fi

  echo "  OK $(cat "${tmp}")"
  rm -f "${tmp}"
}

START=""
END=""
POSITIONAL=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --force)
      FORCE=true
      shift
      ;;
    --days)
      if [[ $# -lt 2 ]]; then
        echo "Error: --days requires a number." >&2
        exit 1
      fi
      DAYS="$2"
      shift 2
      END="$(utc_today)"
      while [[ $# -gt 0 ]]; do
        case "$1" in
          --end)
            END="$(normalize_day "$2")"
            shift 2
            ;;
          --force)
            FORCE=true
            shift
            ;;
          *)
            echo "Error: unknown option: $1" >&2
            exit 1
            ;;
        esac
      done
      if ! [[ "${DAYS}" =~ ^[0-9]+$ ]] || [[ "${DAYS}" -lt 1 ]]; then
        echo "Error: --days must be a positive integer." >&2
        exit 1
      fi
      offset=$((DAYS - 1))
      START="$(date -u -j -f "%Y-%m-%d" -v-"${offset}"d "${END}" +%Y-%m-%d 2>/dev/null || date -u -d "${END} - ${offset} days" +%Y-%m-%d)"
      ;;
    *)
      POSITIONAL+=("$1")
      shift
      ;;
  esac
done

if [[ -z "${START:-}" ]]; then
  if [[ ${#POSITIONAL[@]} -eq 0 ]]; then
    usage
    exit 1
  fi
  START="$(normalize_day "${POSITIONAL[0]}")"
  END="${POSITIONAL[1]:-$(utc_today)}"
  END="$(normalize_day "${END}")"
fi

if [[ "${START}" > "${END}" ]]; then
  echo "Error: START (${START}) must be <= END (${END})." >&2
  exit 1
fi

SECRET="$(resolve_secret)"
if [[ -z "${SECRET}" ]]; then
  echo "Error: AnalyticsDashboardSecret is empty." >&2
  exit 1
fi

echo "Backfilling ${START} .. ${END} on ${HOST}"
echo "Types: ${TYPES}"
if [[ "${FORCE}" == "true" ]]; then
  echo "Mode: force (skip existence checks)"
else
  echo "Mode: skip existing aggregates"
fi
echo

current="${START}"
day_index=0
skipped=0
backfilled=0
while :; do
  day_index=$((day_index + 1))
  echo "[${day_index}] ${current}"

  if [[ "${FORCE}" == "true" ]]; then
    types_to_run="${TYPES}"
  else
    if ! types_to_run="$(missing_types_for_day "${current}" "${SECRET}")"; then
      echo "  SKIP (all requested types already have data)"
      skipped=$((skipped + 1))
      if [[ "${current}" == "${END}" ]]; then
        break
      fi
      current="$(add_one_day "${current}")"
      continue
    fi
    echo "  Missing: ${types_to_run}"
  fi

  backfill_day "${current}" "${SECRET}" "${types_to_run}"
  backfilled=$((backfilled + 1))

  if [[ "${current}" == "${END}" ]]; then
    break
  fi
  current="$(add_one_day "${current}")"
done

echo
echo "Done. Processed ${day_index} day(s): ${backfilled} backfilled, ${skipped} skipped."
