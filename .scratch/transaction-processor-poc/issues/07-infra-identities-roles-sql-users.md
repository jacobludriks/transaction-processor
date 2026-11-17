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
