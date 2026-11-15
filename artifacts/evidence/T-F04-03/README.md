# Evidência — T-F04-03 (Implementar scheduler e inventário)

Estado: em revisão técnica delegada (aguardando CI nativa Linux/Windows do PR #6, `codex/m2-wave1-sensors-contracts-cq`).

## Escopo implementado
- `SensorCaptureAgenda` + `ObservationBatch`: coordenação determinística de múltiplos sensores (`RateHz`, sequência por sensor e orçamento de armazenamento virtual parando em `VirtualStorageFull` sem estourar limites) e lotes imutáveis serializáveis com `schema_version = 1`.
- `FeatureInventoryTriangulator`: desprojeção de pixel com correção radial iterativa e conversão para raios ENU via `EstimatedBodyPose` + `CameraMountEstimate` (sem `TruthSample`), triangulação por interseção de dois raios (`paralaxe >= 5.0°`) ou plano de suporte conhecido (`ângulo rasante >= 5.0°`), e diagnóstico explícito `InsufficientGeometry` em quadro único, geometria degenerada (`< 5.0°`) ou quadro rejeitado.
- Modos `Arcade` vs `Technical`: confirmação automática no modo Arcade e retenção `Unconfirmed` fora do modo técnico até confirmação explícita de classe/feição.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter FeatureInventoryTests --no-restore
```

