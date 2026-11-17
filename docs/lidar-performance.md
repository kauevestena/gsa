# Desempenho de CPU do LiDAR e das consultas de nuvem

`KombiLidarProfilerAcquisition` reutiliza os buffers temporários de retornos e identidades entre os pulsos de uma aquisição. Cada chamada mantém seus próprios buffers. A sequência aleatória, os retornos múltiplos, a chuva e os registros publicados permanecem iguais.

`PointCloudOctreeAndChunkStore.QueryScientificSamples` agrupa referências encontradas pela octree por chunk antes de carregar os dados. Cada chunk correspondente é carregado no máximo uma vez por consulta; os registros são recolocados nas posições originais da travessia. Isso evita alternar repetidamente entre chunks quando o cache comporta apenas um deles. Os filtros inclusivos, as coordenadas em `double`, a revisão, os limites do cache e a verificação SHA-256 de cada recarga são preservados.

O cache contabilizado pela classe continua sendo o cache de chunks residentes. O índice, o backing serializado, as referências temporárias da consulta e a coleção retornada também ocupam memória no processo.

## Medição reproduzível

Com o SDK fixado preparado conforme [toolchain](toolchain.md), execute na raiz do repositório:

```sh
export DOTNET_ROOT="$PWD/work/toolchain/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore Gsa.Domain.slnf --locked-mode
dotnet build src/Gsa.Tools/Gsa.Tools.csproj --no-restore -t:Rebuild -m:1 -p:Optimize=true -p:UseSharedCompilation=false
DOTNET_TieredCompilation=0 dotnet src/Gsa.Tools/bin/Debug/net10.0/gsa-tools.dll run-lidar-performance work/lidar-performance.json --runs=5
```

Usamos a configuração `Debug` com otimização habilitada para reutilizar os locks existentes. `Rebuild` evita reutilizar DLLs sem otimização deixadas pela verificação normal. O relatório registra se a otimização JIT está desabilitada; essa propriedade deve ser `false` para comparar os números publicados. O comando não altera dependências. Em Windows, use as variáveis de ambiente equivalentes no PowerShell.

O protocolo `LIDAR-CPU-V1` faz um aquecimento e entre 1 e 20 execuções medidas por cenário. A configuração padrão usa cinco execuções. Preparação da nuvem, ingestão, GC forçada entre execuções, serialização, hash dos resultados e escrita do relatório ficam fora dos timers. A medição de aquisição inclui a criação do simulador e a geração dos registros.

| Cenário | Entrada e resultado esperado |
| --- | --- |
| `overlapping_chunk_query` | 131.072 amostras em dois chunks sobrepostos; cache de 3.145.728 bytes; consulta estreita com 512 resultados cruzando várias folhas. |
| `resident_chunk_query` | A mesma consulta, com os dois chunks residentes. |
| `resident_full_cloud_query` | Consulta de todas as 131.072 amostras, com os chunks residentes. |
| `continuous_l6_rain_swath` | Dez revoluções L6, 32.000 raios primários, movimento em uma caixa de concreto, chuva e seed `88421`; 24.644 registros neste fixture. |

O relatório inclui tempos individuais, bytes alocados na thread medida, quantidade retornada, número de recargas e hash SHA-256 da serialização de **todos** os registros, em ordem. A opção `scientific_results_deterministic` verifica repetibilidade dentro de uma execução. Para comprovar equivalência entre revisões, compare também os hashes dos dois relatórios. Os testes verificam o limite estrutural de recargas, integridade, ordem, chuva e retornos múltiplos; não usam limites de tempo sujeitos à carga da CI.

Para repetir o baseline `fe6936665767477c62c985b977097b1776ac39ac`, prepare um worktree dessa revisão e copie apenas `src/Gsa.Tools/Benchmark/LidarPerformanceTool.cs` e `src/Gsa.Tools/ToolApplication.cs` da revisão otimizada. Compile e execute a mesma CLI nesse worktree com as mesmas opções. Essa cópia fornece o instrumento sem modificar o domínio antigo. Execute as revisões sequencialmente, sem builds, testes ou outros benchmarks concorrentes.

Os resultados desta comparação e o resumo de validação ficam em [artifacts/evidence/performance-lidar-cpu](../artifacts/evidence/performance-lidar-cpu/). São microbenchmarks de CPU e alocação de objetos; não medem FPS, draw calls, GPU ou desempenho na máquina de referência. Esta otimização atua nas bibliotecas de aquisição contínua e processamento de nuvens; o scanner visual da cidade usa `LidarBenchSimulator` e não é medido por esses cenários.
