# Native M25 performance summary

RTX 3070; Release; VSync off; 30 warm-up + 180 measured frames per row. CPU wall-clock host frame with synchronous Vulkan submissions, not GPU timestamp measurements.

| Resolution | HUD | p50 ms | p95 ms | p99 ms | Worst ms | Mean draws | Field projection total ms | Sprite uploads |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 720p | Off | 6.943 | 7.069 | 11.825 | 12.969 | 115.87 | 3.2787 | 0 |
| 720p | On | 6.944 | 6.977 | 19.304 | 21.098 | 388.72 | 0.6640 | 0 |
| 1080p | Off | 6.973 | 9.948 | 16.919 | 19.801 | 116.32 | 2.5073 | 0 |
| 1080p | On | 6.943 | 6.974 | 6.982 | 21.878 | 387.55 | 0.5908 | 0 |
| 1440p | Off | 6.943 | 6.982 | 9.616 | 12.255 | 116.65 | 2.3889 | 0 |
| 1440p | On | 6.944 | 7.014 | 8.428 | 20.420 | 388.23 | 0.5658 | 0 |
