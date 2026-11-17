#!/usr/bin/env bash
# Builds the metrics.json that `RunReport analyze` reads, from Application Insights and Azure Monitor.
#
# Needs: az (signed in), the `application-insights` az extension, jq. Run it from the laptop or from the tooling VM.
# It only calls the Application Insights query API and the Azure Monitor metrics API (monitoring plane). It does not
# touch the Service Bus, Event Hubs or SQL data planes, so no public network access to them is needed.
#
# Usage:
#   ./export-metrics.sh --app <app insights name> --resource-group <rg> --servicebus <namespace name> \
#       --start 2026-11-17T09:55:00Z --end 2026-11-17T10:25:00Z --out metrics.json [--topic transactions-in]
#
# --start should be a few minutes before the generator started; --end a few minutes after the subscription drained.
set -euo pipefail

APP="" RG="" SB="" START="" END="" OUT="metrics.json" TOPIC="transactions-in"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --app) APP="$2"; shift 2 ;;
    --resource-group) RG="$2"; shift 2 ;;
    --servicebus) SB="$2"; shift 2 ;;
    --start) START="$2"; shift 2 ;;
    --end) END="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --topic) TOPIC="$2"; shift 2 ;;
    *) echo "Unknown option $1" >&2; exit 2 ;;
  esac
done
for v in APP RG SB START END; do
  [[ -n "${!v}" ]] || { echo "Missing required option for $v (see usage at the top of this script)" >&2; exit 2; }
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

kql() { sed "s/__START__/$START/; s/__END__/$END/" "$here/queries/$1"; }

query() {
  az monitor app-insights query --app "$APP" --resource-group "$RG" \
    --start-time "$START" --end-time "$END" --analytics-query "$(kql "$1")" -o json
}

echo "Querying outcomes..." >&2
outcomes="$(query outcomes.kql | jq '[.tables[0].rows[] | {timestamp: .[0], outcome: .[1], reason: (if .[2] == "" then null else .[2] end), count: .[3]}]')"

echo "Querying latency buckets..." >&2
buckets="$(query latency-buckets.kql | jq '[.tables[0].rows[] | {timestamp: .[0], le: .[1], count: .[2]}]')"

sb_id="$(az servicebus namespace show --name "$SB" --resource-group "$RG" --query id -o tsv)"
metric() {
  az monitor metrics list --resource "$sb_id" --metric "$1" --aggregation "$2" --interval PT1M \
    --filter "EntityName eq '$TOPIC'" --start-time "$START" --end-time "$END" -o json
}

# Guard: for a topic subscription Azure Monitor reports the EntityName dimension as the TOPIC name, not the subscription
# name. A filter on a value that does not exist does not fail: it returns a full series of zeros, which the report would
# read as "no backlog and no dead-letters". So check the value really exists before trusting the series.
entities="$(az monitor metrics list --resource "$sb_id" --metric ActiveMessages --aggregation Average --interval PT1M \
  --filter "EntityName eq '*'" --start-time "$START" --end-time "$END" -o json \
  | jq -r '[.value[0].timeseries[]?.metadatavalues[]?.value] | unique | join(", ")')"
if ! grep -qwF -- "$TOPIC" <<<"$entities"; then
  echo "ERROR: Azure Monitor reports no Service Bus entity named '$TOPIC' in this window (entities with data: ${entities:-none})." >&2
  echo "       Use --topic with one of those names. Refusing to export zero-filled backlog and dead-letter series." >&2
  exit 3
fi

echo "Reading the subscription's active-message metric (backlog)..." >&2
backlog="$(metric ActiveMessages Average | jq '[.value[0].timeseries[0].data[]? | select(.average != null) | {timestamp: .timeStamp, activeMessages: .average}]')"

echo "Reading the subscription's dead-letter metric..." >&2
dlq="$(metric DeadletteredMessages Maximum | jq '[.value[0].timeseries[0].data[]? | select(.maximum != null) | {timestamp: .timeStamp, messages: .maximum}]')"

jq -n --argjson outcomes "$outcomes" --argjson buckets "$buckets" --argjson backlog "$backlog" --argjson dlq "$dlq" \
  '{binSeconds: 60, outcomes: $outcomes, latencyBuckets: $buckets, backlog: $backlog, deadLetterQueue: $dlq}' > "$OUT"

echo "Wrote $OUT: $(jq '[(.outcomes | length), (.latencyBuckets | length), (.backlog | length), (.deadLetterQueue | length)] | @csv' -r "$OUT") (outcome, bucket, backlog and dead-letter rows)." >&2
