# Aurelian Marionette session bootstrap

The local Marionette transport executable exposes a developer scenario for the
symbolic `ed-m2b2d` session fixture:

```powershell
dotnet run --project src/Aurelian/Aurelian.Marionette.Transport/Aurelian.Marionette.Transport.csproj -- \
  session-bootstrap --config <ignored-local-config.json>
```

The local configuration contains only the presenter profile, token, and client
name. It does not contain a save path or filename. TSPack creates the ignored
config for a run-scoped Skyrim session bootstrap from its selected host profile.

The scenario connects, authenticates, pings, checks main-menu state, requests
`load_development_session("ed-m2b2d")`, then polls session state and Skyrim
state. Runtime-state queries may transiently time out while Skyrim changes
worlds; that is not treated as a load failure. The scenario succeeds only after
native lifecycle readiness reports PlayerCharacter `0x14`, player 3D, and a
completed post-load state query. It records whether Skyrim was foreground at
the request and readiness points and disconnects gracefully.
