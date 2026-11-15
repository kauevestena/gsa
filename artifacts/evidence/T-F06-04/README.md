# Evidência — T-F06-04 (Implementar estrelas e valor de entrega)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #7 (`d5ba4ea`, run `43675243347`); ver `artifacts/evidence/M2-wave2-ci/summary.json`.

## Escopo implementado
- `DeliveryQuoteCalculator` (`PaymentQuote`, `DeliveryMode`) em `src/Gsa.Missions/Quality/DeliveryQuoteCalculator.cs`:
  - Agregação de margens normalizadas excluindo requisitos binários (`IsBinaryLimit`), mapeando estrelas de `1..5` com tolerância IEEE-754 em todas as fronteiras e pontuação máxima em `>= 0.98` (5★ exige todas as margens completas).
  - Bloqueio explícito em falha de privacidade (`privacy_redacted`), formato (`format_valid`) ou conjunto vazio, inclusive em entrega parcial.
  - Suporte explícito a entregáveis parciais (`1..2` estrelas ponderadas pelo peso aceito, sem desbloqueios de história) e multa linear com teto entre `DueAfterSeconds` e `ExpiresAfterSeconds` com arredondamento único (`Floor`).

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter DeliveryQuoteCalculatorTests --no-restore
```

