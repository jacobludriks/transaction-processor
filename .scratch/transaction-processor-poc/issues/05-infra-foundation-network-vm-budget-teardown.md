# 05: Infrastructure foundation: network, tooling VM, budget and teardown

**What to build:** the base of the Azure environment as Bicep, deployable and deletable with one command each. It creates the Australia East resource group, a VNet with three subnets (App Service VNet integration, private endpoints, tooling VM), the private DNS zones (one shared by Service Bus and Event Hubs, one for SQL), and a small Linux tooling VM (about 4 vCPU) with SSH restricted to the operator's IP. It also creates the budget with alerts at AU$20, AU$35 and AU$45, and a single command that deletes everything. Before building on it, it confirms that .NET 10 is available on App Service Linux in Australia East (otherwise .NET 8 is the fallback) and which services support zone redundancy there. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] One command deploys the foundation and one command deletes the whole resource group, with nothing left behind
- [ ] The VNet has the three subnets, and the DNS zones are linked to the VNet
- [ ] SSH to the tooling VM works from the operator's IP and is refused from elsewhere
- [ ] Budget alerts exist at AU$20, AU$35 and AU$45
- [ ] The tooling VM has the .NET 10 SDK (or runtime) and the tools needed to run the seeder, load generator and SQL scripts
- [ ] The result of the .NET 10 availability check and the zone-redundancy check for each service in Australia East is recorded in the Bicep README or deployment notes. If .NET 10 isn't available, the fallback is noted
- [ ] A what-if run is clean before the first real deployment
