#!/bin/bash
# EXAMPLE: the step windows below are hard-coded from the 2026-11-17 benchmark; edit them (and the resource names) before reuse.
# Per-step resource metrics: max of the per-minute values inside each step's steady window.
RG=rg-txproc-poc
PLAN=$(az appservice plan show -g $RG -n txproc-plan --query id -o tsv | tr -d '\r')
SQL=$(az sql db show -g $RG -s txproc-sql-dxzdew -n txproc --query id -o tsv | tr -d '\r')
SB=$(az servicebus namespace show -g $RG -n txproc-sb-dxzdew --query id -o tsv | tr -d '\r')
S=2026-11-17T11:50:00Z; E=2026-11-17T12:17:00Z
get() { az monitor metrics list --resource "$1" --metric "$2" --aggregation "$3" --interval PT1M --start-time $S --end-time $E -o json 2>/dev/null; }
stat() { # json agg from to  -> max and avg over the window
  jq -r --arg a "$2" --arg f "$3" --arg t "$4" '[.value[0].timeseries[0].data[]? | select(.timeStamp >= $f and .timeStamp <= $t) | .[$a] // empty] | if length==0 then "n/a" else "max \(max|.*10|round/10) avg \((add/length)|.*10|round/10)" end' <<<"$1"
}
declare -A W=( [1000c48-3i-2MU]="2026-11-17T11:55:00Z 2026-11-17T11:58:00Z" [1000c48-6i-2MU]="2026-11-17T12:05:00Z 2026-11-17T12:08:00Z" [1300c48-6i-2MU]="2026-11-17T12:11:00Z 2026-11-17T12:14:00Z" )
for item in "plan CpuPercentage average" "plan MemoryPercentage average" "sql cpu_percent Maximum" "sql workers_percent Maximum" "sql sessions_percent Maximum" "sql log_write_percent Maximum" "sql physical_data_read_percent Maximum" "sb NamespaceCpuUsage Maximum" "sb NamespaceMemoryUsage Maximum" "sb ThrottledRequests Total" "sb ServerErrors Total" "sb ActiveConnections Maximum" "sb IncomingMessages Total" "sb OutgoingMessages Total"; do
  set -- $item; case $1 in plan) R=$PLAN;; sql) R=$SQL;; sb) R=$SB;; esac
  agg=$3; key=$(echo "$agg" | tr 'A-Z' 'a-z'); J=$(get "$R" "$2" "$agg")
  line="$1/$2:"
  for r in 1000c48-3i-2MU 1000c48-6i-2MU 1300c48-6i-2MU; do set -- ${W[$r]}; line="$line  [$r] $(stat "$J" "$key" "$1" "$2")"; done
  echo "$line"
done
