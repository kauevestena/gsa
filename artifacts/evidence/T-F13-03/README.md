# Evidence — T-F13-03 (Oficina de calibração: intrínsecos, extrínsecos, tempo e Arcade/Técnico)

## Escopo
Solucionador de mínimos quadrados com amortecimento Levenberg-Marquardt (`DampedLeastSquaresSolver`: até 50 iterações, passo relativo `1e-6` / mínimo `1e-8`, `lambda0 = 1e-3`, fator `10x`, parada `< 1e-8`) para intrínsecos de câmera (`CalibrateCameraIntrinsics`, `>= 3` poses distintas + validação held-out `<= 1.5 px`), extrínsecos ponto-plano entre duas passadas (`CalibrateExtrinsicsPointToPlane`, `>= 3` planos não paralelos e observabilidade de jacobiano, `<= 0.05 m`), sincronização temporal em `[-0.1, 0.1] s` a `1 ms` e refinamento `0.1 ms` exigindo variação cinemática (`<= 0.02 m`), além da oficina offline (nível `>= 3`) em modos Arcade (mesmo solver com taxa reduzível pela trilha de Calibração) e Técnico (`WorkshopAdjustmentSession` com resíduos observáveis, dicas, desfazer e certificação baseada exclusivamente nos parâmetros resultantes).

## Implementação
- [CalibrationWorkshopAndSolvers.cs](../../../src/Gsa.Missions/Office/CalibrationWorkshopAndSolvers.cs)

## Testes e Verificação
- [M4CalibrationWorkshopAndSolversTests.cs](../../../tests/Gsa.Missions.Tests/M4CalibrationWorkshopAndSolversTests.cs) (`T_F13_03_*`)
- Executado `dotnet test Gsa.Domain.slnf --no-restore`: `148/148` em `Gsa.Missions.Tests` e `405/405` no domínio completo, e `python3 scripts/verify_architecture.py` (`valid: true`).

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #14](https://github.com/kauevestena/gsa/pull/14) (SHA testado `dd961522960f09c6cea723c086f9fa6fdb8c2e92`, merge `ecc1b7c2b3c52202c734c91f124c5443131936c3`, execução [43685741329](https://github.com/kauevestena/gsa/actions/runs/43685741329), jobs Linux `132725728029` e Windows `132725728185`); ver [resumo CI](../M4-wave2-ci/summary.json).

