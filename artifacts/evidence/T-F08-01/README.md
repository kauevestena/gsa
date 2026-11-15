# Evidência — T-F08-01 (Implementar ledger e pagamentos)

Estado: implementado em `codex/m2-wave2-stars-economy-persistence-settlement`; em revisão (aguardando CI do PR #7).

## Escopo implementado
- `CareerLedger` (`CareerLedgerState`, `LedgerEntry`, `LedgerOperationResult`) em `src/Gsa.Missions/Economy/LedgerAndWallet.cs`:
  - Moeda em `int` (Coords) sem saldo ou dívida negativos.
  - Replay idempotente por `TransactionId` preservado através de `ExportState()` / serialização JSON.
  - Subsídio de taxas em contrato calmo abaixo do limiar de `50` Coords, abatimento de dívida limitado a `20%` do pagamento líquido, aplicação de multas (parcela excedente vira `FineDebtCoords`) e recuperação gratuita do kit inicial (`RecoverStarterKitFree`).

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter LedgerAndWalletTests --no-restore
```

