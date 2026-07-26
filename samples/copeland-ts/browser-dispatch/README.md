# Copeland browser dispatch counter

This static-browser trial demonstrates the smallest durable Copeland state law:

```text
state + event + reducer -> next state
```

Build the ESM graph, then serve this folder through HTTP:

```powershell
dotnet run --project .\Copeland.Browser.Dispatch.csproj
python -m http.server 4173 --directory .
```

`dispatch<State, Event>` retains exactly one state value in the browser host. It
calls the authored pure reducer for each event, replaces its retained state with
the returned value, and renders only when the returned value has a new identity.
The host never inspects application fields. `Counter.ts` owns the state shape,
the event enum, and the transition; `Main.ts` only mounts, wires events, and
renders. No mutable closure capture is involved.
