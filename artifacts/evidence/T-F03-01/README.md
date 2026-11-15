# Evidência — T-F03-01 (Locomoção inicial e entradas)

Estado: aceita após aprovação na CI do PR #3 (`6ca633eb54e63844b8261ccbc8932c0cdbd16680`, execução `43670010849`, merge `684a3bf`); ver `artifacts/evidence/M1-mobility-camera-ci/summary.json`.

## Escopo verificado
- `PlatformProfile`: modo a pé (`OnFoot`, velocidade máxima `3.0 m/s`) e carrinho nível 0 (`Handcart`, velocidade máxima `2.0 m/s`, armazenamento virtual `200` unidades, autonomia física `1800 s`, preço `0` Coords, `SupportsLidarSlots = false`).
- `InputActionMap`: nove ações canônicas (`accelerate`, `brake`, `steer_left`, `steer_right`, `interact`, `capture`, `map`, `horn`, `pause`), bindings teclado/gamepad, bloqueio de duplicatas, serialização/desserialização `schema_version: 1` e integração com `Godot.InputMap` via `MobilityBenchAdapter`.
- `LocomotionSimulator` e `MobilityBenchAdapter`: percurso com subida (`slopeGrade`), curva, colisão com `walk_obstacle` sem penetração, embarque/desembarque condicional a velocidade `< 0.1 m/s` e saída livre, interação contextual (`survey_control` apenas a pé, `unload_point`, `office_entry`), e parada explícita da captura ao esgotar armazenamento virtual ou autonomia.

## Reprodução
```sh
python3 scripts/verify.py --exports host
```
Verifica os testes unitários `MobilityTests` em `tests/Gsa.Core.Tests/` e o marcador `GSA_MOBILITY_SMOKE_OK` no projeto Godot e no export Linux nativo headless (`native_smoke.linux = "passed_headless"` em `artifacts/logs/linux/report.json`; `network_isolation = "not_tested"` localmente no Ubuntu 24.04 porque `unshare --user` exige permissão de kernel/AppArmor). Inspeção gráfica interativa continua registrada separadamente.

