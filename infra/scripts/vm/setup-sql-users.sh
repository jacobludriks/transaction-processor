#!/usr/bin/env bash
# Creates the processor and tooling managed identities as SQL users and grants
# them access (see setup-sql-users.sql). Run ON THE TOOLING VM, signed in as the
# Microsoft Entra admin of the SQL server (the account passed to deploy.ps1 as
# -SqlAdminName). Safe to run repeatedly. No password is used or accepted.
#
# Sign in as the admin first:   az login
# (use `az login --use-device-code` over SSH). Then:
#
#   ./setup-sql-users.sh \
#     --server <sql-server>.database.windows.net \
#     --processor-name txproc-processor-id \
#     --tooling-name   txproc-tooling-id
#
# Options (or the matching environment variable):
#   --server <fqdn>            SQL_SERVER           required
#   --database <name>          SQL_DATABASE         default txproc
#   --processor-name <name>    PROCESSOR_IDENTITY_NAME   required
#   --tooling-name <name>      TOOLING_IDENTITY_NAME     required
#   --use-sid                  Create users with the identity's client ID instead of looking the
#                              name up in Entra. Use this if CREATE USER ... FROM EXTERNAL PROVIDER
#                              fails because the SQL server cannot read the directory.
#   --processor-client-id <id> PROCESSOR_CLIENT_ID  required with --use-sid
#   --tooling-client-id <id>   TOOLING_CLIENT_ID    required with --use-sid
set -euo pipefail

SERVER="${SQL_SERVER:-}"
DATABASE="${SQL_DATABASE:-txproc}"
PROCESSOR_NAME="${PROCESSOR_IDENTITY_NAME:-}"
TOOLING_NAME="${TOOLING_IDENTITY_NAME:-}"
PROCESSOR_CLIENT_ID="${PROCESSOR_CLIENT_ID:-}"
TOOLING_CLIENT_ID="${TOOLING_CLIENT_ID:-}"
USE_SID=0

usage() { awk 'NR>1 && /^#/ {sub(/^# ?/, ""); print; next} NR>1 {exit}' "$0"; exit "${1:-2}"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --server) SERVER="$2"; shift 2 ;;
    --database) DATABASE="$2"; shift 2 ;;
    --processor-name) PROCESSOR_NAME="$2"; shift 2 ;;
    --tooling-name) TOOLING_NAME="$2"; shift 2 ;;
    --processor-client-id) PROCESSOR_CLIENT_ID="$2"; shift 2 ;;
    --tooling-client-id) TOOLING_CLIENT_ID="$2"; shift 2 ;;
    --use-sid) USE_SID=1; shift ;;
    -h|--help) usage 0 ;;
    *) echo "Unknown option: $1" >&2; usage ;;
  esac
done

[[ -n "$SERVER" && -n "$PROCESSOR_NAME" && -n "$TOOLING_NAME" ]] || { echo "--server, --processor-name and --tooling-name are required." >&2; usage; }

# Values are substituted into T-SQL text, so accept only plain identifiers and GUIDs.
name_re='^[A-Za-z0-9][A-Za-z0-9_.-]*$'
guid_re='^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
host_re='^[A-Za-z0-9][A-Za-z0-9.-]*$'
[[ "$SERVER" =~ $host_re ]]         || { echo "Invalid --server: $SERVER" >&2; exit 2; }
[[ "$DATABASE" =~ $name_re ]]       || { echo "Invalid --database: $DATABASE" >&2; exit 2; }
[[ "$PROCESSOR_NAME" =~ $name_re ]] || { echo "Invalid --processor-name: $PROCESSOR_NAME" >&2; exit 2; }
[[ "$TOOLING_NAME" =~ $name_re ]]   || { echo "Invalid --tooling-name: $TOOLING_NAME" >&2; exit 2; }

zero_guid='00000000-0000-0000-0000-000000000000'
if [[ "$USE_SID" == 1 ]]; then
  [[ "$PROCESSOR_CLIENT_ID" =~ $guid_re && "$TOOLING_CLIENT_ID" =~ $guid_re ]] \
    || { echo "--use-sid needs --processor-client-id and --tooling-client-id (GUIDs)." >&2; exit 2; }
else
  PROCESSOR_CLIENT_ID="$zero_guid"; TOOLING_CLIENT_ID="$zero_guid"
fi

command -v sqlcmd >/dev/null || { echo "sqlcmd not found. Is this the tooling VM, and has /var/lib/tooling-ready appeared?" >&2; exit 1; }

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Authenticate as the signed-in Azure CLI user. Restrict the default credential
# chain to the Azure CLI so sqlcmd does not pick up the VM's managed identity.
export AZURE_TOKEN_CREDENTIALS=AzureCLICredential
sql_admin=(sqlcmd -S "$SERVER" -d "$DATABASE" --authentication-method ActiveDirectoryDefault -b)

echo "== Connecting to $SERVER / $DATABASE as the Entra admin"
who="$("${sql_admin[@]}" -h -1 -W -Q "SET NOCOUNT ON; SELECT SUSER_SNAME();" | tr -d '\r' | head -n1)"
echo "Connected as: $who"
if [[ "${who,,}" == "${PROCESSOR_NAME,,}" || "${who,,}" == "${TOOLING_NAME,,}" ]]; then
  echo "Connected as a managed identity, not the Entra admin. Run 'az login' as the admin and retry." >&2
  exit 1
fi

echo "== Creating users and granting roles (use-sid=$USE_SID)"
"${sql_admin[@]}" -i "$here/setup-sql-users.sql" \
  -v ProcessorName="$PROCESSOR_NAME" ToolingName="$TOOLING_NAME" \
     ProcessorClientId="$PROCESSOR_CLIENT_ID" ToolingClientId="$TOOLING_CLIENT_ID" \
     UseSid="$USE_SID"

echo "== Done. The users and their roles are listed above."
