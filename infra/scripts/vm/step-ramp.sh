#!/bin/bash
# Step-up load test driver and helpers used for the 2026-11-17 benchmark. Run on the tooling VM. Usage: RATES="450 600 750" OUTD=steps ./step-ramp.sh <tooling-client-id>
# Step-up load test driver. Runs steps one after another and stops when a step shows saturation.
# usage: steps.sh <tooling-client-id>
CID="$1"
NS=txproc-sb-dxzdew.servicebus.windows.net
RG=rg-txproc-poc; SBN=txproc-sb-dxzdew
DUR=360
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd ~/gen
sub() { az servicebus topic subscription show -g $RG --namespace-name $SBN --topic-name transactions-in -n processor --query "[countDetails.activeMessageCount,countDetails.deadLetterMessageCount]" -o tsv 2>/dev/null | tr -d '\r' | tr '\n' ' '; }
mkdir -p ~/runs/${OUTD:-steps}; : > ~/runs/${OUTD:-steps}/steps.log
for RATE in ${RATES:-450 600 750 900 1100 1400}; do
  echo "$(date -u +%H:%M:%SZ) STEP $RATE start; before: $(sub)" >> ~/runs/${OUTD:-steps}/steps.log
  dotnet LoadGenerator.dll load --namespace $NS --client-id $CID --rate $RATE --duration $DUR --unmatched-ratio 0.10 --senders 4 --batch-size 100 --seed $RATE --summary-file ~/runs/${OUTD:-steps}/step-$RATE.json > ~/runs/${OUTD:-steps}/step-$RATE.log 2>&1
  END_STATE=$(sub); ACTIVE=${END_STATE%% *}
  echo "$(date -u +%H:%M:%SZ) STEP $RATE generator done; at end (active dlq): $END_STATE" >> ~/runs/${OUTD:-steps}/steps.log
  # drain wait (max 10 min)
  for i in $(seq 1 60); do S=$(sub); A=${S%% *}; [ "$A" -le 5 ] 2>/dev/null && break; sleep 10; done
  echo "$(date -u +%H:%M:%SZ) STEP $RATE drained: $(sub)" >> ~/runs/${OUTD:-steps}/steps.log
  DL=$(sub); DL=${DL##* }
  # saturation: backlog at end of generator above 15 s of load, or any dead-letter
  if [ "${ACTIVE:-0}" -gt $((RATE*15)) ] 2>/dev/null; then echo "$(date -u +%H:%M:%SZ) STOP: backlog at end of step $RATE was $ACTIVE (saturated)" >> ~/runs/${OUTD:-steps}/steps.log; break; fi
  if [ "${DL:-0}" -gt 0 ] 2>/dev/null; then echo "$(date -u +%H:%M:%SZ) STOP: dead-letters $DL after step $RATE" >> ~/runs/${OUTD:-steps}/steps.log; break; fi
done
echo "$(date -u +%H:%M:%SZ) DONE" >> ~/runs/${OUTD:-steps}/steps.log
