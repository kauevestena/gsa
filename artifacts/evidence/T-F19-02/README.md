# Evidência — T-F19-02 (Validar primeira experiência completa)

Estado: Implementado localmente; aceitação final delegada ao CI multiplataforma do PR de M2 Wave 4.

## Escopo implementado
- `M2PlaytestAndGateValidator` (`M2PlaytestSessionRecord`, `M2CorrectionItem`, `M2GateReport`) em `src/Gsa.Missions/Diagnostics/M2PlaytestAndGateValidator.cs` e comando CLI `M2GateTool` (`run-m2-gate`) em `src/Gsa.Tools/Campaign/M2GateTool.cs`:
  - Validação estrutural do loop técnico M2 completo (`HIST-01` -> descarregamento -> processamento -> CQ -> retrabalho -> entrega -> pagamento -> save round-trip -> seleção de novo contrato `HIST-02`) vinculados às 15 tarefas de M2 (`T-F04-03` .. `T-F19-02`) e registro de correções verificadas (`CORR-M2-01` .. `CORR-M2-03`).
  - Avaliação normativa da coorte de playtest contra os limiares de `specs/validacao.md` (`5` participantes sem experiência prévia em geomática, `<= 30` min por sessão, `>= 4/5` conclusão com `<= 2` intervenções, `>= 4/5` identificação de causa e ação corretiva, `>= 3/5` escolha de outro contrato), rejeitando participantes inelegíveis e emitindo `M2CorrectionItem` sem flexibilizar limiares.
  - Honestidade de evidência: a execução automatizada registra `technical_loop_status: "passed"` e `playtest_gate_status: "pending_human_playtest_cohort"` até a realização da coorte presencial de 5 novatos com `IsHumanCohortSession = true`.

## Artefato gerado
- Relatório consolidado do gate M2: `artifacts/evidence/T-F19-02/m2-gate-report.json`.

## Reprodução
```sh
dotnet test Gsa.Domain.slnf --filter M2PlaytestAndGateTests --no-restore
dotnet src/Gsa.Tools/bin/Debug/net10.0/gsa-tools.dll run-m2-gate content game/world/tutorial-block.json artifacts/evidence/T-F10-01/tutorial-execution-report.json artifacts/evidence/T-F19-02/m2-gate-report.json
```

