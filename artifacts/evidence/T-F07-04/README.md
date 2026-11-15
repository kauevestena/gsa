# Evidência — T-F07-04 (Implementar máscaras e conformidade)

Estado: implementado em `codex/m2-wave3-office-masks-hud-board`; em revisão (aguardando CI do PR #8).

## Escopo implementado
- `PrivacyMaskingService` (`SyntheticSensitiveRegion`, `PrivacyComplianceManifest`) em `src/Gsa.Missions/Office/PrivacyMaskingService.cs`:
  - Extração determinística de regiões sensíveis (`PrivacyMasks` e `ObservedFeatures` com `RequiresPrivacyReview == true`).
  - Aplicação automática em modo `Arcade` ou mediante confirmação por alvo em modo `Technical`, gravando redação RGB (`32, 32, 32`) apenas na cópia derivada da miniatura `256×144` e registrando hashes SHA-256 antes/depois sem alterar capturas brutas.
  - Região sensível pendente (`IsCompliant == false`) reprova `privacy_redacted` no `QualityEvaluator` e bloqueia entrega integral ou parcial no `DeliveryQuoteCalculator`.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter PrivacyMaskingTests --no-restore
```

