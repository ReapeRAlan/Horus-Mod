#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <server-root> <dedicated-config.json> [duration-minutes] [evidence-root]" >&2
  exit 2
fi

server_root="$(realpath "$1")"
config_path="$(realpath "$2")"
duration_minutes="${3:-2}"
evidence_root="${4:-$server_root/runtime-evidence/linux}"
ready_timeout_seconds="${HORUS_READY_TIMEOUT_SECONDS:-300}"
max_log_bytes="${HORUS_MAX_LOG_BYTES:-16777216}"
server_executable="$server_root/NuclearOptionServer.x86_64"
steam_runtime="$server_root/linux64/steamclient.so"

[[ -x "$server_executable" ]] || { echo "Linux server executable is missing or not executable: $server_executable" >&2; exit 1; }
[[ -f "$steam_runtime" ]] || { echo "The official 64-bit Steam runtime is missing: $steam_runtime" >&2; exit 1; }
file "$steam_runtime" | grep -Fq 'ELF 64-bit' || { echo "The Steam runtime is not a 64-bit ELF library: $steam_runtime" >&2; exit 1; }
[[ -f "$config_path" ]] || { echo "Dedicated configuration not found: $config_path" >&2; exit 1; }
grep -Eq '"ModdedServer"[[:space:]]*:[[:space:]]*true' "$config_path" || { echo "The runtime configuration must set ModdedServer=true." >&2; exit 1; }
[[ "$ready_timeout_seconds" =~ ^[0-9]+$ ]] && (( ready_timeout_seconds >= 10 )) || { echo "HORUS_READY_TIMEOUT_SECONDS must be an integer of at least 10." >&2; exit 1; }
[[ "$max_log_bytes" =~ ^[0-9]+$ ]] && (( max_log_bytes >= 1048576 )) || { echo "HORUS_MAX_LOG_BYTES must be an integer of at least 1048576." >&2; exit 1; }

# Match the official RunServer.sh loader path. The depot root also contains a
# 32-bit steamclient.so that must never be selected by the 64-bit server.
export LD_LIBRARY_PATH="$server_root/linux64${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

stamp="$(date -u +%Y%m%d-%H%M%S)"
evidence="$evidence_root/$stamp"
mkdir -p "$evidence"
unity_log="$evidence/server.log"
metrics="$evidence/metrics.csv"
printf 'utc,rss_kib,cpu_percent\n' > "$metrics"

arguments=("-batchmode" "-nographics" "-logFile" "$unity_log" "-DedicatedServer" "$config_path")
started_utc="$(date -u +%FT%TZ)"
ready_observed=false
runtime_failure=""
cd "$server_root"
if [[ -x "$server_root/run_bepinex.sh" ]]; then
  "$server_root/run_bepinex.sh" "$server_executable" "${arguments[@]}" &
elif [[ -x "$server_root/RunServer.sh" ]]; then
  "$server_root/RunServer.sh" "${arguments[@]}" &
else
  echo "BepInEx launcher not found. Install the official BepInEx 5 Unix package first." >&2
  exit 1
fi
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true' EXIT

deadline=$((SECONDS + duration_minutes * 60))
ready_deadline=$((SECONDS + ready_timeout_seconds))
while (( SECONDS < deadline )); do
  sleep 10
  if ! kill -0 "$server_pid" 2>/dev/null; then
    runtime_failure="Dedicated server exited early."
    break
  fi
  ps -p "$server_pid" -o rss=,%cpu= | awk -v now="$(date -u +%FT%TZ)" '{gsub(/^ +| +$/, ""); split($0,a,/ +/); print now "," a[1] "," a[2]}' >> "$metrics"
  if [[ -f "$unity_log" ]]; then
    log_bytes="$(stat -c %s "$unity_log")"
    if (( log_bytes > max_log_bytes )); then
      runtime_failure="Unity log exceeded the ${max_log_bytes} byte safety limit (observed ${log_bytes} bytes)."
      break
    fi
    if [[ "$ready_observed" == false ]] && grep -Fq 'Waiting for Players before loading next map' "$unity_log"; then
      ready_observed=true
    fi
  fi
  if [[ "$ready_observed" == false ]] && (( SECONDS >= ready_deadline )); then
    runtime_failure="Dedicated server did not become ready within ${ready_timeout_seconds} seconds."
    break
  fi
done

kill "$server_pid" 2>/dev/null || true
wait "$server_pid" 2>/dev/null || true
trap - EXIT

if [[ -f "$server_root/BepInEx/LogOutput.log" ]]; then
  cp "$server_root/BepInEx/LogOutput.log" "$evidence/BepInEx.LogOutput.log"
fi
sha256sum "$server_executable" "$config_path" "$server_root/BepInEx/plugins/Horus/Horus.Server.dll" "$server_root/BepInEx/plugins/Horus/Horus.Shared.dll" > "$evidence/SHA256SUMS"
cat > "$evidence/runtime-status.json" <<EOF
{
  "startedUtc": "$started_utc",
  "completedUtc": "$(date -u +%FT%TZ)",
  "requestedDurationMinutes": $duration_minutes,
  "readyTimeoutSeconds": $ready_timeout_seconds,
  "maxLogBytes": $max_log_bytes,
  "readyObserved": $ready_observed,
  "runtimeFailure": "${runtime_failure//\"/\\\"}"
}
EOF
if [[ -n "$runtime_failure" ]]; then
  echo "$runtime_failure" >&2
  exit 1
fi
grep -Fq 'Waiting for Players before loading next map' "$unity_log" || { echo "Required ready marker missing." >&2; exit 1; }
grep -RFiq 'Horus Dedicated Server' "$evidence" || { echo "Horus load marker missing." >&2; exit 1; }
if grep -REin 'unhandled exception|nullreferenceexception|stackoverflowexception|outofmemoryexception|failed to load.*Horus' "$evidence"; then
  echo "Fatal runtime finding detected." >&2
  exit 1
fi
printf 'Linux runtime evidence: %s\n' "$evidence"
