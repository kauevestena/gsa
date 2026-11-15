# Evidência — T-F19-01 (Instrumentação e benchmark do protótipo M1)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #5 (`8294536`, run `43672603212`), com gate de desempenho `pending_reference_hardware` aguardando máquina de referência Radeon Vega 7 não-headless; ver `artifacts/evidence/M1-wave4-ci/summary.json`.

## Escopo verificado
- Instrumentação de domínio em `Gsa.Core.Diagnostics` (`BenchmarkModels`, `BenchmarkHardwareProbe`, `BenchmarkRunner`) e probe em runtime Godot (`EngineBenchmarkHarness` sob `_benchRoot`) com registro explícito de counters (`visible_triangles`, `draw_calls`, `active_vehicles=50`, `active_pedestrians=30`, `lidar_points_per_sec`, `sensor_queue_backlog_seconds`, `deadlock_removals`, `dropped_samples`, `silent_sample_drops=0`) e timers (`frame_cpu_ms`, `frame_gpu_ms`, `physics_step_ms`, `sensor_raycast_ms`, `thumbnail_capture_ms`, `block_assembly_ms`) e memória residente.
- Protocolo reproduzível de **3 execuções de 5 minutos (`300 s`) após `30 s` de aquecimento** em `60 Hz` (`18000` ticks medidos por execução) para os dois presets estáticos de M1 (`dry_day` e `rain_night`), cobrindo o quarteirão piloto (`20` fachadas, `1` ponto de descarregamento, `9` módulos do kit glTF), `50` carros, `30` pedestres, câmera a `2 Hz` e bancada LiDAR (`lidar-bench` 1000 rps, `lidar-l5` 8000 rps, `lidar-l6` 16000 rps, todos com `offered_in_mvp_shop=false`).
- **Defeito real detectado e corrigido durante o benchmark:** a primeira execução (`3×300 s`) detectou crescimento linear de memória na segunda metade (`+30.9` a `+32.9 MiB` por execução) causado por retenção ilimitada de miniaturas RGB (`256×144×3`) em `CameraCaptureScheduler._samples`. Adicionado `CameraCaptureScheduler.FlushBatchManifest` (e cache de padrão imutável em `LidarProfile`), reduzindo o drift da segunda metade para `-5.67` a `-4.37 MiB` com pico de `92.75 MiB` e `continuous_memory_growth_detected=false`.
- Hardware real registrado honestamente (`Ubuntu 24.04.2 LTS`, `Intel(R) Core(TM) i7-10750H`, `62.5 GiB` RAM, `NVMe SSD`, modo headless sem GPU gráfica), resultando em `reference_hardware_matched=false` e `gate_status="pending_reference_hardware"` em vez de declarar aceite em máquina incompatível.

## Sumário das medições ([`benchmark-report.json`](benchmark-report.json))
- Build / Toolchain: `Release`, `.NET 10.0.15`, Godot `4.7.2`, VSync `false`, resolução alvo `1920x1080`, backend `Compatibility`.
- Montagem do quarteirão (`old_center_tutorial_block`): `assembly_ms = 5.6028 ms`, `world_objects = 48`, `facade_count = 20`, `unload_zone_count = 1`, `art_kit_modules = 9` (`648` triângulos), `total_scene_triangles = 127224` (`dry_day`) / `129624` (`rain_night`).
- Execuções (`dry_day` × 3 e `rain_night` × 3): `frame_cpu_ms` média `0.0831–0.1073 ms`, `p95` `0.1479–0.2511 ms`, `p99` `0.4129–0.5319 ms`, `memory_peak_mb` `91.23–92.75 MiB`, `second_half_memory_drift_mb` `-5.668` a `-4.371 MiB`, `silent_sample_drops = 0`.
- Limitação explícita do ambiente: `frame_gpu_ms` permanece `0` nas execuções headless de CLI e CI; medição gráfica com Radeon Vega 7 real continua pendente no gate.

## Reprodução
```sh
dotnet build src/Gsa.Tools/Gsa.Tools.csproj -c Release --no-restore
dotnet src/Gsa.Tools/bin/Release/net10.0/gsa-tools.dll run-m1-benchmark game/world/tutorial-block.json game/world/pilot-kit artifacts/evidence/T-F19-01/benchmark-report.json
python3 scripts/verify.py --exports host
```
Verifica `BenchmarkTests`, `BenchmarkToolTests`, o comando `run-m1-benchmark` e o marcador `GSA_BENCHMARK_SMOKE_OK` no projeto Godot e no export Linux nativo headless.

