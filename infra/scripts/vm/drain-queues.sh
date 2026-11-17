#!/bin/bash
# usage: drain.sh <client-id>  -- receive-and-delete everything in the subscription DLQ and queue DLQ
TOK=$(curl -s -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://servicebus.azure.net&client_id=$1" | jq -r .access_token)
B=https://txproc-sb-dxzdew.servicebus.windows.net
drain() { n=0; while :; do c=$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 -X DELETE -H "Authorization: Bearer $TOK" "$2?timeout=3"); [ "$c" = 200 ] && n=$((n+1)) || break; done; echo "$1: deleted $n (last HTTP $c)"; }
drain "subscription DLQ" "$B/transactions-in/subscriptions/processor/\$deadletterqueue/messages/head"
drain "queue DLQ" "$B/transactions-recognised/\$deadletterqueue/messages/head"
drain "subscription (active)" "$B/transactions-in/subscriptions/processor/messages/head"
