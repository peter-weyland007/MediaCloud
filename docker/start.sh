#!/usr/bin/env bash
set -euo pipefail

API_PORT="${API_PORT:-5299}"
WEB_PORT="${WEB_PORT:-8080}"

export ApiBaseUrl="${ApiBaseUrl:-http://127.0.0.1:${API_PORT}}"

raw_conn="${ConnectionStrings__DefaultConnection:-}"
raw_conn="${raw_conn%\"}"
raw_conn="${raw_conn#\"}"
raw_conn="${raw_conn%\'}"
raw_conn="${raw_conn#\'}"

if [[ -z "${raw_conn// }" ]]; then
  export ConnectionStrings__DefaultConnection="Data Source=/app/data/mediacloud.db"
elif [[ "$raw_conn" =~ ^[Dd]ata[[:space:]]+[Ss]ource= ]]; then
  db_path="${raw_conn#*=}"
  db_path="${db_path# }"

  if [[ "$db_path" != /* ]]; then
    db_path="/app/data/${db_path#./}"
  fi

  if [[ "$db_path" != *.db ]]; then
    db_path="${db_path%/}/mediacloud.db"
  fi

  export ConnectionStrings__DefaultConnection="Data Source=$db_path"
elif [[ "$raw_conn" == *=* ]]; then
  export ConnectionStrings__DefaultConnection="$raw_conn"
else
  db_path="$raw_conn"
  if [[ "$db_path" != /* ]]; then
    db_path="/app/data/${db_path#./}"
  fi
  if [[ "$db_path" != *.db ]]; then
    db_path="${db_path%/}/mediacloud.db"
  fi
  export ConnectionStrings__DefaultConnection="Data Source=$db_path"
fi

mkdir -p /app/data

echo "Using DB: ${ConnectionStrings__DefaultConnection}"
echo "Starting API on :${API_PORT}"
(
  cd /app/api
  dotnet api.dll --urls "http://0.0.0.0:${API_PORT}"
) &
API_PID=$!

echo "Starting Web on :${WEB_PORT} (API=${ApiBaseUrl})"
(
  cd /app/web
  dotnet web.dll --urls "http://0.0.0.0:${WEB_PORT}"
) &
WEB_PID=$!

cleanup() {
  kill "$API_PID" "$WEB_PID" 2>/dev/null || true
}
trap cleanup TERM INT

wait -n "$API_PID" "$WEB_PID"
STATUS=$?
cleanup
exit $STATUS
