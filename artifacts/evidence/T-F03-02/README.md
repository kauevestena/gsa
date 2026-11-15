# Evidência — T-F03-02 (Galinha Gimbalina e estresse)

Estado: aceita após aprovação na CI do PR #3 (`6ca633eb54e63844b8261ccbc8932c0cdbd16680`, execução `43670010849`, merge `684a3bf`); ver `artifacts/evidence/M1-mobility-camera-ci/summary.json`.

## Escopo verificado
- `GimbalinaStabilizer`: medidor de estresse em `[0, 100]`, invariante `Injured = false`, eventos de susto (`Horn +10`, `Pothole +15`, `Dog +20`, `Fireworks +25`), repouso `-5/s` e milho `-30` com cooldown de `10 s`.
- Estabilização de cabeça limitada a `±45°` de contrarrotação e oscilação linear de `0°` (em `<= 60`) até `5°` (em `100`), transferindo velocidade angular à atitude da câmera durante a captura.
- Replay comparativo calmo vs estressado no mesmo trecho demonstrando aumento causal mensurável de `motion_blur_px`, além de fuga segura não letal após `5 s` contínuos em estresse `100` e recuperação segura no escritório.

## Reprodução
```sh
python3 scripts/verify.py --exports host
```
Verifica `GimbalinaStressTests` em `tests/Gsa.Core.Tests/` e o marcador `GSA_GIMBALINA_SMOKE_OK` no projeto e no export Linux nativo headless (`native_smoke.linux = "passed_headless"`, `network_isolation = "not_tested"` no Ubuntu 24.04).

