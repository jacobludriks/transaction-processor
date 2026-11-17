# 07: Identities, roles and SQL users

**What to build:** passwordless access for the processor and the tooling, with no connection strings anywhere. Two user-assigned managed identities. The processor identity can receive from the topic subscription, send to the output queue, send to the event hub, and read the two lookup tables. The tooling identity (on the VM) can send to the input topic, receive from the event hub and the dead-letter path, and write to the database for seeding. The SQL users for each identity are created by name, which needs a Microsoft Entra admin on the SQL server and runs from the tooling VM. Spec: `.scratch/transaction-processor-poc/spec.md`.

**Blocked by:** 06

**Status:** ready-for-agent

- [ ] Both identities and all of their role assignments are created by Bicep, and the SQL users by a script run from the VM
- [ ] The SQL server has an Entra admin configured, and the setup step documents who it is
- [ ] The processor identity has read-only access to the two tables and cannot write to them
- [ ] The tooling identity can write to the database, send to the input topic, and receive from the event hub and the dead-letter path
- [ ] From the VM, using the tooling identity, a test message can be sent to the topic and a test row written to the database
- [ ] From the VM, using the processor identity, a test write to the database is refused
- [ ] No connection string or password appears in any template, script or setting, and applications are pointed at an identity by its client ID

## Progress note (offline implementation)

Bicep for the two user-assigned identities and seven role assignments is in `infra/`, wired into the same deployment. Each role is scoped to one resource: the processor identity receives on the subscription and sends to the output queue and event hub; the tooling identity sends to the topic, receives on the subscription and queue (so it can read both dead-letter queues) and receives on the event hub. Role definition IDs were checked against Microsoft's built-in roles reference. The tooling identity is attached to the VM, and so is the processor identity by default (a parameter), because the "refused write" check has to run from the VM. Both identities' name, client ID, principal ID and resource ID are deployment outputs for ticket 08. The SQL users come from an idempotent script run on the VM as the Entra admin (contained users by name, with a client-ID fallback if the server cannot read the directory); the processor gets read only and the tooling identity gets read, write and DDL for seeding. A verification script for the VM and a results table are documented in the infra README. The Bicep passes `az bicep build`, `az bicep lint` and `az bicep build-params`, the scripts pass `bash -n`, and a grep finds no connection string or password in infra. No criterion is ticked because each needs a real deployment, a VM session or both to confirm. The Entra-admin-documentation criterion is covered in the README but needs the deployed admin to confirm.
