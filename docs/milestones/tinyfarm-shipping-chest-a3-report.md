# TinyFarm A3 — containers and shipping chest

Outcome: **Success**. The normal native launch now includes a shipping chest beside the farmhouse, just north of the starter garden. Face it and press **E** to open a two-table inventory window. Click a row to transfer one item, or choose **Move stack**. Either table can transfer into the other. **Escape** or **Close** closes the window and the chest lid.

## Behavior

- The chest is an authored **Object** agent with an **Idle** Dominatus controller, its existing identity inventory and product stacks, a container capability and closed/open pose. There is no separate chest inventory store.
- The window reuses Machina `UiDataTable` and `StandardUI.Button`. Left/Right chooses a side, Up/Down chooses a row, Enter transfers. Each table has independent pagination. Inventory mutation is never performed by the table.
- Key items cannot move in either direction. Explicit `IsKeyItem` metadata is available on identity items, product definitions and authored item seeds. Legacy delivery letters and recipe cards are protected too.
- Equipped gear must be unequipped in the existing inventory UI before transfer. Both the UI and authoritative reducer enforce this.
- Contents with positive sale value convert to the beneficiary's coins at **09:00 once per day**. Pickup also stamps an empty chest, preventing late deposits from selling during the same day. Zero-value products remain available to retrieve.
- A persisted pickup day and receipt prevent duplicate payout after save/load. The receipt shows the last pickup's item count and coins. Sleeping across a pickup runs the missed collection before arriving at next morning; it does not collect next morning's shipment before 09:00.
- If a payout cannot fit the integer currency/count bounds, contents remain intact and that pickup is stamped; they can be retrieved or wait for a future pickup.
- Direct store sales are disabled in A3. Historical campaigns retain their original economy behavior.

## Authority and authoring

`TinyFarmContainerState` extends the shared agent state with shipping policy, beneficiary, opener and receipt. Typed `OpenContainerIntent`, `CloseContainerIntent` and `TransferContainerIntent` reduce in Core alongside all other gameplay intents. Transfer checks include capability, opener, proximity, scene, ownership, key metadata, equipped state, quantities and destination capacity before any mutation. Identity items update both their owner and inventory membership; product stacks use the existing count reducer. Shipping follows the existing time reducer, including voluntary sleep and automatic night return.

The window captures gameplay through the existing InputMan ActionMap stack and pauses the simulation. Its UI preferences are transient. Open pose and opener are saved; loading an open chest reopens its window without changing the restored semantic hash. Save validation rejects invalid opener/pose combinations, recipients and pickup dates. Replay uses the existing source-generated polymorphic intent graph.

`TinyFarmShippingContent` composes A2 and uses `TinyFarmAgentTemplate.Object(...).Container` to instantiate the shipping agent. A plain `new TinyFarmContainerState()` creates storage without automatic sales. Collision remains authored in the scene: the shipping chest has a 1 × 1 tile footprint at (7,4), with its sprite feet/agent interaction point at (7.5,5). Changing its sprite scale does not change collision. Agent inspection and the properties screen expose its contents, pose, policy, pickup day and receipt.

`TinyFarmChestArt.cs` provides a small repo-owned vector sprite realized into two 256 × 256 cells: closed and open. The existing RGBA resource/upload, grid sprite metadata, linear sampler and actor-depth renderer present it. This is initial functional art; its flat vector finish can receive a painterly pass later. No existing approved art was regenerated.

## Save compatibility

Normal native launches use version 16, runtime `tiny-farm-containers@16`, and a new **sleeping-spring-a3** slot. A2 saves are preserved in their existing slot; this task does not migrate their content provenance into A3. Historical proof switches continue loading their original content.

## Validation

- Full normal Release suite: **436 TinyFarm + 27 Spatial2D tests passed**, zero failures. Historical canonical hashes remain covered.
- **17 container tests passed with JSON reflection disabled**, covering actual table clicks, one/stack moves, ownership conservation, key/equipment protection, stale requests, closed/distant/wrong-scene requests, capacity, generic storage, currency overflow, daily timing, empty pickup, late deposits, sleeping over pickup, real host-clock pickup, paused input isolation, save validation, open-window restoration and generated save/replay.
- Native Release build with `JsonSerializerIsReflectionEnabledByDefault=false`: zero warnings/errors.
- Visible Vulkan walkthrough on NVIDIA RTX 3070: Enter starts; walk to and harvest the starter turnip; E opens the chest; actual Machina row clicks deposit/retrieve/deposit; save and restore the open window; close; advance the authoritative clock to 09:00; receive **5 coins**; save/load and advance without a duplicate payout; reopen and inspect the receipt.
- Native 1920 × 1080 and 2560 × 1440 framebuffer sizes were asserted; screenshots and the two-pose sprite sheet are in `artifacts/tinyfarm-shipping-chest-a3/`. Native proof uses real visible-window focus and portable injected key/pointer queues; it does not automate OS event delivery.
- `git diff --check` passed.

An additional full-suite reflection-disabled audit exposed **27 older proof tests** using pre-existing reflection serialization. Representative failure: `TinyFarmCanonicalScenario.Prove`, line 69. That file is unchanged by this task. The new container runtime, save/replay graph, focused tests and native proof all use source-generated serialization and pass with reflection disabled. The historical proof migration is not claimed complete; details are recorded in `legacy-reflection-audit.json`.

Reproduce the native walkthrough after building:

```powershell
dotnet build Games/TinyFarm/TinyFarm.Native/TinyFarm.Native.csproj -c Release -m:1 -p:JsonSerializerIsReflectionEnabledByDefault=false
dotnet Games/TinyFarm/TinyFarm.Native/bin/Release/net10.0-windows/TinyFarm.Native.dll --container-proof
```

Launch normally with `Play-TinyFarm.cmd` to play the A3 loop. No separate executable is needed.
