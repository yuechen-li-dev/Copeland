# Aurelian Skyrim timeline persistence

`SkyrimGameTimestamp.GameDays` comes from
`RE::Calendar::GetCurrentGameTime()`: Skyrim game days, not .NET ticks or wall
time. `SkyrimTimelineStamp` adds semantic session and ordered runtime sequence.
`SkyrimSaveIdentity` uses the reliably known symbolic save name and stamp; it
does not invent a GUID.

Rollback means loaded game days are less than the previous committed value.
Sequence reset after load is allowed. Exact save-name checkpoint matching wins;
otherwise the latest checkpoint not later than the loaded time is selected.
Newer entries remain on disk but leave the active lineage.

Dominatus.Core is the only runtime persistence engine. Aurelian does not
serialize a parallel copy of agent execution state. M3 calls
`DominatusCheckpointBuilder.Capture`, `DominatusSave.CreateCheckpointChunks`,
`SaveFile.Write`, `SaveFile.Read`, `DominatusSave.ReadCheckpointChunks`, and
`DominatusCheckpointBuilder.Restore`. The payload remains the existing `DOM1`
chunked binary.

Skyrim save identity selects a Dominatus checkpoint; it does not replace the
checkpoint format. The JSON index holds only save/timeline facts, artifact
filename/hash, version, creation time, parent, and lineage status. It never
duplicates blackboards, HFSM paths, mailbox state, or agent state.

A legal M3 checkpoint requires `WorldReady`, no restoration uncertainty, no
active exclusive binding, and no unconsumed child return. Fresh restore creates
the owner and placed agents before Dominatus restores primitive blackboards and
stable state paths. Runtime BodyIds/FormIDs and bindings are discarded, so
agents start unbound. Dynamic agents and pending Skyrim mutations are not
restored. Missing, corrupt, and version-mismatched artifacts are explicit.
