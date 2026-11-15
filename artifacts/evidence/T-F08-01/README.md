# Evidência — T-F08-01 (Implementar ledger e pagamentos)

Estado: aceita por revisão técnica delegada após CI nativa Linux/Windows do PR #7 (`d5ba4ea`, run `43675243347`); ver `artifacts/evidence/M2-wave2-ci/summary.json`.

## Escopo implementado
- `CareerLedger` (`CareerLedgerState`, `LedgerEntry`, `LedgerOperationResult`) em `src/Gsa.Missions/Economy/LedgerAndWallet.cs`:
  - Moeda em `int` (Coords) sem saldo ou dívida negativos.
  - Replay idempotente por `TransactionId` preservado através de `ExportState()` / serialização JSON.
  - Subsídio de taxas em contrato calmo abaixo do limiar de `50` Coords, abatimento de dívida limitado a `20%` do pagamento líquido, aplicação de multas (parcela excedente vira `FineDebtCoords`) e recuperação gratuita do kit inicial (`RecoverStarterKitFree`).

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter LedgerAndWalletTests --no-restore
```

