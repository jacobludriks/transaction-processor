#!/bin/bash
# usage: analyze.sh <win-file: "label HH:MM HH:MM" per line> <from ISO> <to ISO>
export MSYS_NO_PATHCONV=1
S="C:/Users/jacob/AppData/Local/Temp/claude/c0b17e91378ca1a9/5e3f4eb7-3d7a-4b0b-9740-d1e975307aaa/scratchpad"
WIN="$1"; FROM="$2"; TO="$3"; D=2026-11-18
RG=rg-txproc-poc
SB=$(az servicebus namespace show -g $RG -n txproc-sb-dxzdew --query id -o tsv); SQL=$(az sql db show -g $RG -s txproc-sql-dxzdew -n txproc --query id -o tsv); PLAN=$(az appservice plan show -g $RG -n txproc-plan --query id -o tsv); EHN=$(az eventhubs namespace show -g $RG -n txproc-eh-dxzdew --query id -o tsv)
get() { az monitor metrics list --resource "$1" --metric "$2" --aggregation "$3" --interval PT1M --start-time $FROM --end-time $TO --query "value[0].timeseries[0].data[].[timeStamp, $4]" -o tsv | awk -v L="$5" '{print L"\t"substr($1,12,5)"\t"$2}'; }
{ get $PLAN CpuPercentage Average average plan_cpu_avg; get $PLAN CpuPercentage Maximum maximum plan_cpu_max; get $PLAN MemoryPercentage Average average plan_mem
get $SQL cpu_percent Average average sql_cpu_avg; get $SQL cpu_percent Maximum maximum sql_cpu_max; get $SQL workers_percent Maximum maximum sql_workers; get $SQL log_write_percent Maximum maximum sql_log; get $SQL physical_data_read_percent Maximum maximum sql_io
get $SB NamespaceCpuUsage Maximum maximum sb_cpu; get $SB NamespaceMemoryUsage Maximum maximum sb_mem; get $SB ThrottledRequests Total total sb_throttled; get $SB ServerErrors Total total sb_errors
get $EHN IncomingMessages Total total eh_in_msgs_per_min; get $EHN IncomingBytes Total total eh_in_bytes_per_min; get $EHN IncomingRequests Total total eh_in_requests_per_min; get $EHN ThrottledRequests Total total eh_throttled; get $EHN ServerErrors Total total eh_server_errors; get $EHN UserErrors Total total eh_user_errors; } > "$S/metrics-eh95.tsv"
echo "== Azure Monitor (avg/peak over steady minutes)"
awk -F'\t' 'NR==FNR { split($0,w," "); n++; name[n]=w[1]; s[n]=w[2]; e[n]=w[3]; hdr=hdr "\t" w[1]; next }
 { for (i=1;i<=n;i++) if ($2>=s[i] && $2<=e[i] && $3!="") { k=name[i] SUBSEP $1; sum[k]+=$3; c[k]++; if (!(k in mx) || $3>mx[k]) mx[k]=$3; labels[$1]=1 } }
 END { print "metric" hdr; for (l in labels) { line=l; for (i=1;i<=n;i++) { k=name[i] SUBSEP l; line=line "\t" (c[k] ? sprintf("%.1f/%.1f", sum[k]/c[k], mx[k]) : "-") } print line } }' "$WIN" "$S/metrics-eh95.tsv" | column -t -s$'\t'
STEPS=$(awk -v D=$D '{printf "%s\"%s\", datetime(%sT%s:00Z), datetime(%sT%s:59Z)", (NR>1?", ":""), $1, D, $2, D, $3}' "$WIN")
echo "== latency percentiles (p50 p95 p99 total)"
Q=$(sed -e "s/__STEPS__/$STEPS/" -e "s/__FROM__/$FROM/g" -e "s/__TO__/$TO/g" "$S/pct.kql"); az monitor app-insights query --app txproc-appi -g $RG --analytics-query "$Q" --query "tables[0].rows" -o tsv
echo "== per minute (msg/s, other outcomes, lat avg, lat max, batch avg, batch max)"
Q=$(sed "s/__START__/$FROM/" "$S/perminute.kql"); az monitor app-insights query --app txproc-appi -g $RG --analytics-query "$Q" --query "tables[0].rows" -o tsv
echo "== outcomes"
Q="customMetrics
| where timestamp > datetime($FROM) and name == \"txproc.messages\"
| summarize total=sum(coalesce(valueSum,value)) by oc=tostring(customDimensions.outcome), rs=tostring(customDimensions.reason)"
az monitor app-insights query --app txproc-appi -g $RG --analytics-query "$Q" --query "tables[0].rows" -o tsv
