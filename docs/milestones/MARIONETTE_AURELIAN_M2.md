# MARIONETTE-AURELIAN-M2

## Outcome

M2 imports a bounded collection of observed Skyrim bodies as deterministic,
session-scoped Aurelian NPC agents. A generated Dominatus 1.0 flow utility
selects one agent, sends it an acquire-body intent, and composes the existing
M1 owner-validated bind/move/release flow for that selected identity.

The live path no longer preselects `Candidates[0]` for semantic action. Native
order is only a deterministic transport order; Dominatus is authoritative for
ranking.

## Ownership after M2

| Capability | Skyrim | Marionette | Aurelian | Dominatus |
| --- | --- | --- | --- | --- |
| candidate materialization | owns actor data | queries and lowers | represents candidates | — |
| agent identity | — | maps opaque body | owns | uses |
| agent provenance | — | supplies source facts | owns | may score |
| candidate eligibility | enforces conservative safety | translates | owns semantic reasons | gates and scores |
| candidate ranking | not authoritative | — | supplies composed data | owns |
| body binding | materializes | lowers | owns lifecycle | executes policy |
| movement decision | — | — | supplies observations | owns |
| movement mutation | executes | lowers | selected agent commands | awaits |
| release/restoration | executes | lowers | owns result | branches |
| mailbox/events | emits engine facts | publishes | owns contracts | consumes |

## Scoped Skyrim proof

On 2026-08-03 the installed `tspack.exe` rejected `--dominatus-skyrim` because
it predates commit `77de99c`. Running the same command from current tspack
source successfully built, tested, staged, launched only `C:\SkyrimDev\Game`,
observed the plugin ready marker, and later verified restoration of both the
runtime config and Skyrim INI. It did not execute the managed scenario:
`launchSkyrim` waits for the game process to exit and never starts
`Aurelian.Marionette.Transport dominatus-skyrim`. No movement or binding claim
was inferred from the ready marker.

The gap was then closed with the explicit managed bootstrap/controller steps
below. The disposable `ed-m2b2d` session reported two eligible bodies. They
lowered to two opaque body IDs and two imported agent IDs. Dominatus selected
agent `ef2089ad-3372-5406-94fd-c4483ef02ec1`; a non-selected agent command was
rejected; the selected agent bound body
`skyrim-body-8df39a557ddb370f86afca73`, moved it from 64 to 16 units from the
goal, observed `accepted -> in_progress -> completed`, released it, and
restored player/camera to `0x14`. Native correlation IDs were
`ef2089ad3372540694fdc4483ef02ec0` (bind),
`ef2089ad3372540694fdc4483ef02ec3` (move), and
`ef2089ad3372540694fdc4483ef02ec2` (release). Tspack subsequently verified
runtime-config and Skyrim-INI restoration. This is live body-binding proof,
not possession.

## Exact operator checklist

1. Build the current tspack source or install a binary containing `77de99c`.
2. Run `go run ./cmd/tspack run skyrim --dominatus-skyrim --json --root
   C:\SkyrimDev\Plugins\MarionetteSSE` from the tspack repository.
3. After `ED_M2B2_PIPE_LISTENING`, run `dotnet run --project
   C:\Users\yuech\source\repos\Copeland\src\Aurelian\Aurelian.Marionette.Transport\Aurelian.Marionette.Transport.csproj
   -- dominatus-skyrim --config
   C:\SkyrimDev\Plugins\MarionetteSSE\build\msse-presenter-m1\aurelian-transport.json`.
4. Require at least two candidate agent IDs and opaque body IDs in the report.
5. Capture named utility factors, selected agent/body, and adapter-only
   FormID/generation diagnostics.
6. Require the selected binding to reach `Bound`, a non-selected agent move to
   be rejected, movement to complete with reduced distance, and release to
   reach `Released`.
7. Require player and camera target restoration to `0x14`, then close the
   isolated game so tspack can verify config and INI restoration.

Do not run against the Steam installation and do not describe the result as
possession.

## Limits

M2 does not add durable save identity, navigation, animation locomotion,
active-session persistence, player rebinding, input transfer, content TOML,
or a general engine agent. The next milestone should make backend connection,
world readiness, body loss, and restoration-required events an explicit small
engine-owner agent/service and close the tspack managed-controller launch gap.
