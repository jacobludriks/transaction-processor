# 06: Infrastructure data services with private endpoints

**What to build:** the three data services, zone-redundant in Australia East and reachable only over private endpoints. Azure SQL Database (General Purpose, 4 vCores, provisioned, zone-redundant) holding the item and SKU tables; Service Bus Premium with 1 messaging unit, holding the input topic with one subscription (no filters) and the output queue; and Event Hubs Standard with 1 throughput unit and 2 partitions. Public network access is disabled on all three. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 05

**Status:** ready-for-agent

- [ ] SQL, Service Bus and Event Hubs are deployed by Bicep as part of the same one-command deployment and removed by the same teardown command
- [ ] Each service has a private endpoint in the private-endpoint subnet, and the DNS zones resolve each service name to a private address
- [ ] From the tooling VM, each service's name resolves to a private address and a connection succeeds
- [ ] From outside the VNet (the operator's laptop), none of the three services is reachable
- [ ] The Service Bus topic has exactly one subscription with no filters, and the default maximum delivery count of 10 is in effect
- [ ] The SQL tier can be changed by a parameter, without rebuilding the environment, so it can be scaled between runs
- [ ] The zone-redundancy setting for each service is confirmed on the deployed resource, and any service that turned out not to support it is noted
