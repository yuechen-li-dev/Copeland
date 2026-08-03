# CHIM identity and timeline lessons

Two production lessons apply. Runtime FormIDs are not durable across load-order
changes, so placed references need plugin filename plus plugin-local ID.
External agent state also cannot advance independently of Skyrim save history;
loading earlier game time must select earlier state and exclude the abandoned
future branch.

M3 adopts those constraints, not CHIM's storage architecture. It uses Aurelian
provenance, Marionette main-thread observations, and Dominatus.Core checkpoints.
It adds no database, event-sourcing log, or parallel agent snapshot.
