# Copeland Machina M1 settings proof

`dotnet run --project Copeland.Machina.M1.csproj --no-restore` compiles the
ordinary Copeland `Settings.ts` screen through the bounded Machina source
profile and writes `wwwroot/index.html` plus `wwwroot/resolved.txt`.

Serve `wwwroot` through static HTTP. The generated browser page uses only
semantic HTML, explicit absolute frames, generated CSS, and a reducer-owned
state value for the `SettingsEvent.Save` and `SettingsEvent.ToggleDarkMode`
bindings. It has no React, Vue, Blazor, CSS flex, or CSS grid runtime.
