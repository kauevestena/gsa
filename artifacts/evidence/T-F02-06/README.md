# Evidence — T-F02-06 (Cidade e rede geodésica)

## Escopo medido

O domínio monta quatro regiões (~4 km², 400 células) e oito marcos, separados dos checkpoints contratuais. Deslocar um marco altera sua posição física e preserva a referência cadastral.

`EvaluateBenchmarkStreamingWindows` atualiza o modelo neutro de janelas de streaming nas posições dos oito marcos. Mede ida e volta de `LocalMapFrame` em **double** (`< 1e-9 m`) e a identidade geoidal `H = h - Ng`. Não executa navegação, streaming de nós Godot ou aquisição de dados. Seu resultado não inclui contadores fornecidos pelo próprio chamador como suposta evidência de integridade.

## Verificação separada da engine

`FullCityEnginePrecisionVerifier` passa 16 pontos (marcos e deslocamentos fracionários) por `GodotCoordinateAdapter.ToGodot` e `ToMap`, incluindo o `Godot.Vector3` real. O limite é **0,0005 m**, compatível com a quantização float nas distâncias locais da cidade. O smoke local em 2026-11-16 mediu erro máximo **0,0000673664 m** (~0,067 mm). Isso mede a fronteira numérica, não a precisão de física, renderização ou levantamento.

O pipeline exige `GSA_CITY_ENGINE_PRECISION_SMOKE_OK` na cena e no executável exportado.

## Integridade de custódia

`FullCityCustodyIntegrityTests` cria uma observação de câmera e dois lotes reais, um de campo e outro protegido. Ao deslocar cada marco e atualizar as janelas, compara os snapshots completos de custódia, payloads, hashes SHA-256 recalculados e miniaturas. Também verifica a recuperação do snapshot. Não usa nomes ou quantidades de lotes como substituto dos dados.

Esse teste verifica isolamento entre o modelo de mundo e a custódia. Não comprova sobrevivência a descarregamento real de cenas ou navegação humana; essa integração continua pendente.

## Arquivos

- `src/Gsa.Core/World/FullCityAndGeodeticNetworkAssembler.cs`
- `src/Gsa.Engine/World/FullCityEnginePrecisionVerifier.cs`
- `tests/Gsa.Core.Tests/M7Wave1CityVanLidarAndPavementTests.cs`
- `tests/Gsa.Missions.Tests/FullCityCustodyIntegrityTests.cs`

## Estado

Correção de evidências em revisão; aceite remoto depende da CI do SHA correspondente. Não fecha gates humanos ou de desempenho.
