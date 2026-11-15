# Evidence — T-F16-03 (Sabotagem avançada, detector 3-sigma de spoofing, custódia e contramedidas)

## Escopo
Coordenação avançada de sabotagem e contramedidas em `FactionSabotageAndCountermeasuresCoordinator`, `ProgressiveGnssSpoofingDetector` e `MovedBenchmarkRegistry`: spoofing progressivo de GNSS (até `10 m` em `5 s`) com rejeição baseada exclusivamente em inovação Kalman `> 3-sigma`, deslocamento físico de marcos mantendo histórico e referência de contrato intactos enquanto sinaliza discrepância pós-evento, bomba em veículo estacionado preservando `100%` dos lotes em custódia protegida (`ProtectedOffloaded`), invariante de não-violência (`ProtagonistInjured == false`, `GimbalinaInjured == false`), não repetição de ataques após save/reload, contramedidas multiplicativas (`Escolta 0,5x`, `Disfarce Kombi 0,5x` até 1º ataque, janela de madrugada `0,5x`) com piso `>= 0,25x`, autorização exclusivamente via associação de bairro legitimada por habilidade de negociação e cancelamento imediato pelo Modo Cidade Calma sem cobrança indevida.

## Implementação
- [FactionSabotageAndCountermeasuresCoordinator.cs](../../../src/Gsa.Missions/Economy/FactionSabotageAndCountermeasuresCoordinator.cs)

## Testes e Verificação
- [M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs](../../../tests/Gsa.Missions.Tests/M5CloudCqOfficeLidarWorkshopAndSabotageTests.cs) (`T_F16_03_AdvancedSabotageSpoofing3SigmaMovedBenchmarkCustodyBombCountermeasuresAndCalmCity`)

## Estado
Implementada e em verificação para Onda 2 do marco M5.

