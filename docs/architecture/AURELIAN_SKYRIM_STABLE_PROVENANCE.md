# Aurelian Skyrim stable provenance

M3 separates five identities: semantic `AgentId`; imported/authored
`AgentProvenance`; placed `SkyrimPlacedActorOrigin`; current opaque `BodyId`;
and backend-only `HostActorId(FormID, generation)`. Native pointers never cross
the pipe.

`SkyrimPlacedActorOrigin` accepts `.esm`, `.esp`, and `.esl` filenames, rejects
paths, blank names, zero IDs, and values above 24 bits, lower-cases the plugin
name invariantly, and formats local IDs as six hexadecimal digits:
`somemod.esp|012345`. The range includes the 12-bit light-plugin subset.
Native resolution uses `TESForm::GetFile(0)`, `TESFile::GetFilename()`,
`TESForm::GetLocalFormID()`, `TESFile::IsLight()`, and
`TESForm::IsDynamicForm()` on the existing Skyrim main-thread query task.

Placed agent IDs derive from SHA-256 over a versioned domain separator and the
normalized origin. Session and load-order FormID do not participate. Different
plugins separate equal local IDs; rematerialization attaches a new BodyId and
generation to the same agent. Body loss preserves semantic identity.

Dynamic references use `DynamicSessionReference`; their derivation includes the
authenticated session and they are not restored. M2's body-only overload is the
migration adapter for that explicit policy. Display names never form identity.
# M4a restore boundary

Placed plugin/local provenance persists inside the canonical Dominatus
checkpoint. Runtime FormIDs, BodyIds, bindings, and dynamic-session actors do
not. After restore, a fresh Skyrim observation rematerializes the placed origin
onto the restored semantic AgentId in the unbound state.
