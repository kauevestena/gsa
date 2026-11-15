# Evidência — T-F17-01 (Kit piloto e pipeline de importação)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #4 (`4a11e48`, run `43671438816`); ver `artifacts/evidence/M1-wave3-ci/summary.json`.

## Escopo verificado
- Kit modular em `game/world/pilot-kit/` com todos os 9 módulos obrigatórios (`lot`, `sidewalk`, `corner`, `intersection`, `facade`, `pole`, `tree`, `sign`, `manhole`), materiais de teste declarados (`concrete`, `glass`, `water`), preset Blender glTF 2.0 (`blender-gltf-export-preset.json`) com `export_extras=true` e `export_yup=true`, e atlas `textures/pilot_atlas_256.png` (`256×256` px).
- Malhas `.gltf` válidas em glTF 2.0 com contagem real de triângulos por módulo urbano dentro do orçamento (`60..96`, intervalo `50–500`), preservação estrita de `semantic_id`, `semantic_class`, `semantic_material` e colisor em `nodes[0].extras`, além de marcas exclusivamente fictícias.
- `PilotArtKitValidator` em `Gsa.Core.Art`, subcomando CLI `gsa-tools validate-art-kit` em `Gsa.Tools.Art` e verificação em runtime Godot `PilotKitBenchVerifier` empacotada nos presets Linux/Windows.

## Reprodução
```sh
python3 scripts/verify.py --exports host
```
Verifica `ArtKitTests`, `ArtKitToolTests`, o comando `validate-art-kit` e o marcador `GSA_PILOT_KIT_SMOKE_OK` no projeto e export Linux nativo headless.

