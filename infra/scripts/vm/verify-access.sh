#!/usr/bin/env bash
# Proves from the tooling VM that each identity has exactly the access it should.
# Run ON THE TOOLING VM after the SQL users exist (setup-sql-users.sh). Needs the
# tooling AND processor identity both attached to the VM (the template does this
# by default; see attachProcessorIdentityToVm in main.bicep). No secrets: tokens
# come from the VM's identity endpoint, and sqlcmd signs in as each identity.
#
#   ./verify-access.sh \
#     --sql-server <sql>.database.windows.net \
#     --servicebus <sb>.servicebus.windows.net \
#     --eventhubs  <eh>.servicebus.windows.net \
#     --processor-client-id <guid> --tooling-client-id <guid>
#
# Options (defaults match the names in main.bicep):
#   --database txproc  --topic transactions-in  --subscription processor
#   --queue transactions-recognised  --eventhub transactions-unrecognised
#   --keep   leave the scratch table dbo.poc_access_check in place
#
# Each check prints PASS or FAIL. Exit code is 0 only if every check passed.
# Test traffic: one message on the input topic, one on the output queue and one
# event on the event hub (all with a small marker body), plus a scratch table
# that is dropped at the end. Run this before the processor is started, or expect
# the processor to dead-letter the topic message as unparseable XML. The receive
# checks peek-lock the head message (if any) briefly; it is redelivered after the
# lock expires.
set -uo pipefail

SQL_SERVER=""; SERVICEBUS=""; EVENTHUBS=""
DATABASE="txproc"; TOPIC="transactions-in"; SUBSCRIPTION="processor"
QUEUE="transactions-recognised"; EVENTHUB="transactions-unrecognised"
PROCESSOR_CLIENT_ID=""; TOOLING_CLIENT_ID=""; KEEP=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --sql-server) SQL_SERVER="$2"; shift 2 ;;
    --servicebus) SERVICEBUS="$2"; shift 2 ;;
    --eventhubs) EVENTHUBS="$2"; shift 2 ;;
    --database) DATABASE="$2"; shift 2 ;;
    --topic) TOPIC="$2"; shift 2 ;;
    --subscription) SUBSCRIPTION="$2"; shift 2 ;;
    --queue) QUEUE="$2"; shift 2 ;;
    --eventhub) EVENTHUB="$2"; shift 2 ;;
    --processor-client-id) PROCESSOR_CLIENT_ID="$2"; shift 2 ;;
    --tooling-client-id) TOOLING_CLIENT_ID="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    -h|--help) awk 'NR>1 && /^#/ {sub(/^# ?/, ""); print; next} NR>1 {exit}' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done
for v in SQL_SERVER SERVICEBUS EVENTHUBS PROCESSOR_CLIENT_ID TOOLING_CLIENT_ID; do
  [[ -n "${!v}" ]] || { echo "Missing required option for $v (see --help)." >&2; exit 2; }
done
for c in curl jq sqlcmd; do command -v "$c" >/dev/null || { echo "$c not found." >&2; exit 1; }; done

FAILS=0
pass() { echo "PASS  $1"; }
fail() { echo "FAIL  $1  ($2)"; FAILS=$((FAILS+1)); }

# Token for a given identity (by client ID) and audience, from the VM identity endpoint.
imds_token() {
  curl -sS -m 20 -H Metadata:true \
    "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&client_id=$1&resource=$2" \
    | jq -r '.access_token // empty'
}

# HTTP status of a request to Service Bus / Event Hubs as an identity.
#   rest <client-id> <METHOD> <url> [body]
rest() {
  local token; token="$(imds_token "$1" "https://servicebus.azure.net")"
  [[ -n "$token" ]] || { echo "token"; return; }
  curl -sS -m 30 -o /dev/null -w '%{http_code}' -X "$2" "$3" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: application/atom+xml;type=entry;charset=utf-8' \
    --data "${4:-}" 2>/dev/null || echo "000"
}

# expect <label> <actual-status> <allowed statuses...>
expect_ok()      { local label="$1" got="$2"; shift 2; for s in "$@"; do [[ "$got" == "$s" ]] && { pass "$label (HTTP $got)"; return; }; done; fail "$label" "expected one of: $* ; got $got"; }
expect_refused() { local label="$1" got="$2"; [[ "$got" == 401 || "$got" == 403 ]] && pass "$label refused (HTTP $got)" || fail "$label" "expected 401/403; got $got"; }

sql_as() {   # sql_as <client-id> <query>
  sqlcmd -S "$SQL_SERVER" -d "$DATABASE" --authentication-method ActiveDirectoryManagedIdentity -U "$1" -b -h -1 -W -Q "SET NOCOUNT ON; $2" 2>&1
}

echo "== Service Bus"
SB="https://$SERVICEBUS"
expect_ok      "tooling sends to topic $TOPIC"           "$(rest "$TOOLING_CLIENT_ID"   POST "$SB/$TOPIC/messages" '<poc-access-check/>')" 201
expect_refused "processor sends to topic $TOPIC"         "$(rest "$PROCESSOR_CLIENT_ID" POST "$SB/$TOPIC/messages" '<poc-access-check/>')"
expect_ok      "tooling reads subscription dead-letter"  "$(rest "$TOOLING_CLIENT_ID"   POST "$SB/$TOPIC/subscriptions/$SUBSCRIPTION/\$deadletterqueue/messages/head?timeout=5")" 200 201 204
expect_ok      "tooling reads queue $QUEUE"              "$(rest "$TOOLING_CLIENT_ID"   POST "$SB/$QUEUE/messages/head?timeout=5")" 200 201 204
expect_ok      "tooling reads queue dead-letter"         "$(rest "$TOOLING_CLIENT_ID"   POST "$SB/$QUEUE/\$deadletterqueue/messages/head?timeout=5")" 200 201 204
expect_ok      "processor reads subscription $SUBSCRIPTION" "$(rest "$PROCESSOR_CLIENT_ID" POST "$SB/$TOPIC/subscriptions/$SUBSCRIPTION/messages/head?timeout=5")" 200 201 204
expect_ok      "processor sends to queue $QUEUE"         "$(rest "$PROCESSOR_CLIENT_ID" POST "$SB/$QUEUE/messages" '{"check":"poc-access-check"}')" 201
expect_refused "processor reads queue $QUEUE"            "$(rest "$PROCESSOR_CLIENT_ID" POST "$SB/$QUEUE/messages/head?timeout=5")"

echo "== Event Hubs"
EH="https://$EVENTHUBS"
expect_ok      "processor sends to event hub"            "$(rest "$PROCESSOR_CLIENT_ID" POST "$EH/$EVENTHUB/messages?timeout=30" '{"check":"poc-access-check"}')" 201
expect_refused "tooling sends to event hub"              "$(rest "$TOOLING_CLIENT_ID"   POST "$EH/$EVENTHUB/messages?timeout=30" '{"check":"poc-access-check"}')"
echo "NOTE  tooling receive on the event hub is an AMQP operation and is not testable with curl;"
echo "      check it with the throwaway event hub reader, or inspect the role assignment in the portal."

echo "== SQL (scratch table dbo.poc_access_check)"
out="$(sql_as "$TOOLING_CLIENT_ID" "IF OBJECT_ID(N'dbo.poc_access_check') IS NULL CREATE TABLE dbo.poc_access_check (id int IDENTITY PRIMARY KEY, written_by sysname NOT NULL DEFAULT SUSER_SNAME(), written_at datetime2 NOT NULL DEFAULT SYSUTCDATETIME());")"
[[ $? -eq 0 ]] && pass "tooling creates a table" || fail "tooling creates a table" "$out"

out="$(sql_as "$TOOLING_CLIENT_ID" "INSERT dbo.poc_access_check DEFAULT VALUES; SELECT COUNT(*) FROM dbo.poc_access_check;")"
[[ $? -eq 0 ]] && pass "tooling writes a row (rows now: $(echo "$out" | tr -d '\r' | tail -n1))" || fail "tooling writes a row" "$out"

out="$(sql_as "$PROCESSOR_CLIENT_ID" "SELECT COUNT(*) FROM dbo.poc_access_check;")"
[[ $? -eq 0 ]] && pass "processor reads the table" || fail "processor reads the table" "$out"

out="$(sql_as "$PROCESSOR_CLIENT_ID" "INSERT dbo.poc_access_check DEFAULT VALUES;")"; rc=$?
if [[ $rc -ne 0 && "$out" == *"permission was denied"* ]]; then
  pass "processor write refused (permission denied)"
else
  fail "processor write refused" "exit $rc: $out"
fi

out="$(sql_as "$PROCESSOR_CLIENT_ID" "CREATE TABLE dbo.poc_access_check_processor (id int);")"; rc=$?
if [[ $rc -ne 0 && "$out" == *"permission denied"* ]]; then
  pass "processor create table refused (permission denied)"
else
  fail "processor create table refused" "exit $rc: $out"
fi

if [[ "$KEEP" == 0 ]]; then
  out="$(sql_as "$TOOLING_CLIENT_ID" "DROP TABLE IF EXISTS dbo.poc_access_check;")"
  [[ $? -eq 0 ]] && echo "      scratch table dropped" || echo "WARN  could not drop dbo.poc_access_check: $out"
fi

echo
if [[ $FAILS -eq 0 ]]; then echo "All checks passed."; else echo "$FAILS check(s) failed."; fi
exit $(( FAILS > 0 ))
