# CPU e alocações de LiDAR — 16/11/2026

Baseline: `fe6936665767477c62c985b977097b1776ac39ac`. Código candidato: `acd5b58f05fda113c030cabe14a2c3ef686ed6fc`.

Execuções sequenciais no mesmo ambiente Linux, SDK 10.0.404, runtime .NET 10.0.15, configuração Debug com `Optimize=true` e `Rebuild`, `DOTNET_TieredCompilation=0`. Um aquecimento e cinco execuções por cenário. O instrumento foi copiado para um worktree do baseline sem alterações ao domínio antigo. Hashes dos fontes e resultados em [summary.json](summary.json); todas as medições em [before.json](before.json) e [after.json](after.json).

| Cenário | Antes (mediana, ms) | Depois (mediana, ms) | Antes (bytes alocados) | Depois (bytes alocados) |
| --- | ---: | ---: | ---: | ---: |
| Consulta com cache restrito | 111.256 | 9.070 | 83,903,048 | 10,499,944 |
| Consulta estreita residente | 0.175 | 0.178 | 13,376 | 13,736 |
| Consulta completa residente | 4.998 | 3.950 | 3,147,008 | 3,147,560 |
| Aquisição L6 com chuva | 15.605 | 9.455 | 13,608,408 | 4,826,768 |

Na consulta com cache restrito, as recargas caíram de 16 para 2 em cada execução. Os hashes de todos os registros, incluindo ordem, são idênticos nas dez execuções de cada cenário. A pequena diferença da consulta estreita residente está sujeita ao ruído de medição; os números não são limites de aceite.

O [resumo local de validação](verification.json) registra 441 testes aprovados, 22 verificações aprovadas, build sem avisos, locks inalterados e export/smoke Linux headless. A CI do PR complementa essa execução em Linux e Windows. As medições são de bibliotecas e não demonstram ganho de FPS ou aceite na GPU de referência; o scanner visual da cidade usa outro caminho.

[Protocolo, reprodução e limites](../../../docs/lidar-performance.md).
