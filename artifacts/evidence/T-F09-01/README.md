# Evidência — T-F09-01 (Tráfego regular de bancada)

Estado: em revisão técnica delegada antes da CI do PR #4 (`codex/m1-traffic-lidar-art`).

## Escopo verificado
- `RoadNetworkGraph.CreateTutorialBlockGraph()`: grafo direcionado de vias com duas faixas Leste/Oeste conectadas por três avenidas Norte/Sul, calçadas contínuas, faixas de pedestres sinalizadas e zona de exclusão de `6 m` protegendo `office_entry` (`[500232, 7400005, 101.5]`).
- `TrafficBenchSimulator`: simulação determinística em `60 Hz` (`dt = 1/60 s`) com `50` carros e `30` pedestres, controlador semafórico de ciclos EW/NS, respeito a faixas de pedestres e remoção limpa de impasses fora de câmera após `>= 20 s` (`DeadlockRemovals`) mantendo veículos visíveis à câmera intactos.
- `TrafficBenchAdapter`: renderização via `MultiMeshInstance3D` sob `_benchRoot` (fora de `_worldRoot`/`_registry`) e replay completo de 5 minutos (`18000` ticks) sem bloqueio permanente.

## Reprodução
```sh
python3 scripts/verify.py --exports host
```
Verifica `TrafficBenchTests` em `tests/Gsa.Core.Tests/` e o marcador `GSA_TRAFFIC_SMOKE_OK` no projeto Godot e no export Linux nativo headless.

