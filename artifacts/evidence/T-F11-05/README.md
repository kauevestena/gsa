# T-F11-05 — Incremento Geopédia avançada e tablet técnico

Incremento de código de 17/11/2026, com dependências T-F11-03, T-F15-03 e T-F14-04 já aceitas. **A tarefa completa continua aberta:** a cidade ainda não fornece trajetória estimada ao overlay, e screenshots/revisão visual PT-BR/EN-US a 150% não foram executados neste ambiente sem display.

## Entrega revisada

- Catálogo aditivo com 30 artigos (16 anteriores + 14 avançados), quatro campos bilíngues por artigo e 17 novos termos no glossário. Auditoria passa a verificar todos os artigos. IDs anteriores e verbetes de colecionáveis são preservados.
- Leitor da cena com seleção, texto completo, unidades, limitações e bloqueio por conceito/marco. Desbloqueio por plataforma não concede colecionáveis/missões; os IDs avançados passam pelo save v2 real no smoke da cidade.
- Diagnóstico usa o perfil e passo de canais da aquisição: L4 1.000/1.000, L5 8.000/4.000 e L6 16.000/4.000 raios primários/s nominais/programados. Alcance, ruído do perfil e pontos visíveis não são rotulados como precisão ou densidade oficial.
- Requisitos são copiados do contrato aceito e de seus entregáveis, com comparador, unidade, predicado, superfície, célula e densidade-alvo. Formatação local preserva limites pequenos por representação round-trip, sem arredondá-los para zero.
- Atenção usa snapshots do rastreador e limites reais dos territórios, com números/níveis e indicação explícita da proteção tranquila. O mapa não depende só de cor.
- Nuvem pode ser escondida sem parar aquisição. O tablet atualiza diagnóstico a 10 Hz somente quando mapa/Técnico estão visíveis; não reconstrói quadro/frota ou corpos de todos os artigos nesse caminho.

Revisão técnica delegada: comparar textos com [modelos](../../../specs/modelos.md), [perfis LiDAR](../../../src/Gsa.Core/Sensors/Lidar/LidarProfile.cs), [aquisição por fatias](../../../src/Gsa.Core/Sensors/Lidar/LidarBenchSimulator.cs), [tracker de atenção](../../../src/Gsa.Core/Traffic/FactionsAndTerritoryAttention.cs) e requisitos efetivos de contrato. A Geopédia explica os modelos do domínio; não afirma que todos estejam conectados à cena da cidade.

## Evidência automatizada

Comando: `DOTNET_PROCESSOR_COUNT=1 python3 scripts/verify.py --exports host`, ferramentas fixadas no repositório. O [relatório local](local-verification.json) registra testes, builds, export e checksums reais; o [manifesto de código](source-manifest.json) identifica o conteúdo revisado/testado sobre a base indicada no relatório. O marcador novo integra o smoke existente da cidade, evitando inicializar a cidade novamente só para outro teste.

`AdvancedSurveyPresentationTests` compara o ritmo anunciado com os callbacks de raios da execução real por fatias; verifica auditoria bilíngue, desbloqueios/idempotência, IDs recarregados, limites exatos nas duas culturas, entregáveis parciais e imutabilidade dos snapshots de atenção. O smoke interage com seletor e botões reais, troca idiomas, confere ocultação de sistemas indisponíveis, executa aquisição com nuvem escondida e conserva os bytes dos saves. O fixture da carreira é restaurado em seguida.

Logs da [cena](city-tablet-smoke.log) e do [executável Linux](export-city-tablet-smoke-linux.log) incluem `GSA_ADVANCED_SURVEY_SMOKE_OK` e `GSA_CITY_SAVE_SMOKE_OK`. O pipeline exige os dois marcadores no editor e no export nativo de cada plataforma; a CI Linux/Windows do PR deve passar no SHA final antes do merge autorizado. A CI mantém os exports e logs completos como artefatos do workflow.

## Pendências preservadas

Deriva/sigma da trajetória e RMSE oficial ficam explicitamente indisponíveis na cidade com pose ideal. O escritório/contratos da cidade continuam simplificados. Nenhum ganho de FPS/GPU é alegado. A integração de trajetória/CQ, screenshots de cada subsistema, inspeção visual/controle e os gates humanos/de hardware continuam pendentes; este incremento não fecha T-F11-05 ou M7. Uso e limites estão no [guia do tablet](../../../docs/advanced-survey-tablet.md).
