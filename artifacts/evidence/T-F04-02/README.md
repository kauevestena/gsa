# Evidência — T-F04-02 (Observações de câmera e miniaturas)

Estado: em revisão técnica delegada antes da CI do PR #3 (`codex/m1-mobility-camera`).

## Escopo verificado
- `CameraSensorProfile.Initial`: `1280×720`, `2 Hz` (intervalo de 30 ticks em física 60 Hz), FOV horizontal `90°`, exposição dia `1/500 s`, noite `1/60 s`, readout `10 ms`, miniatura `256×144`.
- `CameraVisibilityEvaluator`: amostragem de 5 raios na face frontal, exigindo `>= 3` raios sem oclusão e menor dimensão projetada `>= 16 px`; testado tanto analiticamente quanto com `PhysicsDirectSpaceState3D.IntersectRay` real na cena `TutorialBlock.tscn` (`glass_sample` ocluindo `facade_01`).
- `CameraExposureMetrics`: comparação analítica vs projeção em dois instantes (`blur_px` dentro de `0,1 px`), deslocamento por rolling shutter, clipping/ofuscamento solar `< 10°`, obstrução de lente e ruído noturno.
- `CameraObservationSample`, `CameraThumbnail` e `CameraCaptureBatchManifest`: miniatura `256×144` (`SubViewport` na engine + síntese determinística headless), `full_texture_persisted = false`, máscaras de privacidade em `privacy_review`, fila limitada a `0,5 s` com saturação explícita (`sensor_queue_saturated`) e guarda por reflexão garantindo ausência de `TruthSample` e profundidade.

## Reprodução
```sh
python3 scripts/verify.py --exports host
```
Verifica `CameraObservationTests` em `tests/Gsa.Core.Tests/` e o marcador `GSA_CAMERA_SMOKE_OK` no projeto e no export Linux nativo headless (`native_smoke.linux = "passed_headless"`, `network_isolation = "not_tested"` no Ubuntu 24.04).

