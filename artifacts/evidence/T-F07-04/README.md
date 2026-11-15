# Evidência — T-F07-04 (Implementar máscaras e conformidade)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #8 (`594ddf7`, run `43676281804`); ver `artifacts/evidence/M2-wave3-ci/summary.json`.

## Escopo implementado
- `PrivacyMaskingService` (`SyntheticSensitiveRegion`, `PrivacyComplianceManifest`) em `src/Gsa.Missions/Office/PrivacyMaskingService.cs`:
  - Extração determinística de regiões sensíveis (`PrivacyMasks` e `ObservedFeatures` com `RequiresPrivacyReview == true`).
  - Aplicação automática em modo `Arcade` ou mediante confirmação por alvo em modo `Technical`, gravando redação RGB (`32, 32, 32`) apenas na cópia derivada da miniatura `256×144` e registrando hashes SHA-256 antes/depois sem alterar capturas brutas.
  - Região sensível pendente (`IsCompliant == false`) reprova `privacy_redacted` no `QualityEvaluator` e bloqueia entrega integral ou parcial no `DeliveryQuoteCalculator`.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter PrivacyMaskingTests --no-restore
```

