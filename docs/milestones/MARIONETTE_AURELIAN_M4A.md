# Marionette/Aurelian M4a

M4a closes the M3 callback bridge gap. Marionette now emits correlated,
monotonic save/load observations; Aurelian stages and commits canonical
Dominatus checkpoints and restores a fresh historical world after a successful
load. Failed loads do not restore. New game and revert remain distinct.

Automated proof creates checkpoint A, adds B-only semantic state, creates
checkpoint B, loads A, verifies B becomes inactive without deletion, restores
A's placed AgentId, rejects the old BodyId, and rematerializes a current unbound
body onto that AgentId. The native `DOM1` header is asserted.

The scoped tspack command now launches the long-lived
`live-save-correlation` controller. Interactive menu input remains manual and is
the only unexecuted portion until an operator performs the A/B/load-A sequence.
No possession, player authority, camera, quest, or parallel persistence work is
included.

