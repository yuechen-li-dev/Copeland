# Browser rectangle proof

Build the `browser` target with TSPack, then run `browser-proof`:

```powershell
go run .\cmd\tspack update --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\machina-table-derivation-m0\10-browser-proof
go run .\cmd\tspack sync --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\machina-table-derivation-m0\10-browser-proof
go run .\cmd\tspack build --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\machina-table-derivation-m0\10-browser-proof browser
go run .\cmd\tspack run --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\machina-table-derivation-m0\10-browser-proof browser-proof --once
```

The proof uses the sibling TSPack checkout's Playwright runtime by default (override `TSPACK_PLAYWRIGHT_MODULE` when needed), starts and stops its own local host, reads semantic hosts with `getBoundingClientRect()`, asserts center/gap/edge/expansion geometry with a `0.01px` tolerance, records console/page/request diagnostics, captures a supporting screenshot, and closes browser and host in `finally`.
