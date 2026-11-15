# Evidência — T-F15-01 (Bancada LiDAR)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #4 (`4a11e48`, run `43671438816`); ver `artifacts/evidence/M1-wave3-ci/summary.json`.

## Escopo verificado
- `LidarProfile`: perfis canônicos alinhados a `specs/parametros.md` — `L4` (`1000` raios/s, `50 m`, sigma `0,02 m`), `L5` (`8000` raios/s, `80 m`, sigma `0,01 m`) e `L6` (`16000` raios/s, `120 m`, sigma `0,005 m`), todos com `OfferedInMvpShop = false` e indisponíveis em plataformas de nível `< 4` (`OnFoot`, `Handcart`).
- `LidarMaterialProperties` e `LidarBenchSimulator`: materiais (`asphalt`, `concrete`, `metal`, `glass`, `water`, `vegetation`), atenuação por incidência `max(0,1, cos(ângulo))`, chuva (`-20%` alcance útil e `~2%` retornos espúrios), multi-retorno em materiais transmissivos com passo `0,01 m`, máximo de `3` retornos por pulso e energia residual mínima `0,1` sem duplicar superfícies.
- Georreferenciamento por plano conhecido, boresight, braço de alavanca e deslocamento temporal usando apenas `EstimatedBodyTrajectory` (sem `TruthSample`, validado por reflexão), limite de fila de `0,5 s` (`sensor_queue_saturated`) e raycast físico real em `TutorialBlockScene` via `LidarBenchRig`.

## Reprodução
```sh
python3 scripts/verify.py --exports host
```
Verifica `LidarBenchTests` em `tests/Gsa.Core.Tests/` e o marcador `GSA_LIDAR_SMOKE_OK` no projeto e export Linux nativo headless.

