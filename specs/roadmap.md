# Roadmap por dependências e gates

[Índice](README.md) · [Funcionalidades](features/README.md) · [Validação](validacao.md)

Não há datas comprometidas sem equipe/disponibilidade. Cada gate exige todas as tarefas atribuídas ao marco, seus critérios locais e os cenários integrados aplicáveis ao escopo entregue. Um cenário de funcionalidade futura não bloqueia uma tarefa anterior de schema ou biblioteca: usar fixture isolado e registrar a integração pendente no marco responsável. O marco seguinte começa após o gate anterior; E1 e E2 partem de M9 e são independentes entre si. Planejar ativos/roteiro já previstos em M4 não antecipa a integração de M8.

```mermaid
flowchart LR
    M0[Fundacao] --> M1[Viabilidade]
    M1 --> M2[Contrato completo]
    M2 --> M3[MVP e playtests]
    M3 --> M4[Carro e oficina]
    M4 --> M5[Kombi e MMS]
    M5 --> M6[Picape e ato 2]
    M6 --> M7[Van e campanha]
    M7 --> M8[Conteudo e dublagem]
    M8 --> M9[Versao 1.0]
    M9 --> E1[Exportacoes]
    M9 --> E2[Plataformas especiais]
```

Os gates M2/M3 verificam diversão/aprendizado antes de investir no restante da cidade. Protótipo LiDAR de M1 é técnico e não amplia o MVP. Tarefas de bancada de túnel/viaduto são validadas em M5; sua integração narrativa no ato 2 fecha em M6. A disponibilidade de uma funcionalidade não significa que todo o conteúdo de campanha correspondente já foi produzido.

## Integração jogável atual

Avaliação de **20/11/2026**, após o merge do PR #27. A CI da revisão de `main` gerou e testou os executáveis Linux e Windows. A verificação desta versão registra 508 testes de domínio aprovados e 24 etapas do pipeline local. [Revisão avaliada](https://github.com/kauevestena/gsa/commit/d0f49e088e9c5efeb05b36187cb0cb6dd3b35228) · [CI de main](https://github.com/kauevestena/gsa/actions/runs/44731754445) · [Relatório dos testes](../artifacts/evidence/T-F09-05/city-encounters/validation.json).

| Área | Estado observado | Evidência |
| --- | --- | --- |
| Treinamento | **Três fachadas** já liga fotografias e paralaxe a descarga, processamento, CQ, recoleta, pagamento e recarga do save. Usa poses ideais e um slot próprio. | [Treinamento](../docs/live-training.md) |
| Carreira | São Datum reúne quatro regiões, frota, contratos e tablet bilíngue. Fotos de veículos motorizados usam a pose estimada após a inicialização GNSS/IMU/DMI. O LiDAR visível ainda é uma prévia. O escritório decide a entrega pela proporção de alvos vistos em fotos e, a partir de 85%, emite uma cotação fixa de cinco estrelas. | [Navegação](../docs/live-navigation.md) · [Cálculo da entrega na revisão avaliada](https://github.com/kauevestena/gsa/blob/d0f49e088e9c5efeb05b36187cb0cb6dd3b35228/src/Gsa.Engine/World/SaoDatumTablet.cs#L295-L351) |
| Perigos | Já há atenção de facções, contaminação da lente, alteração da observação GNSS, marcos deslocados, perda de veículo e proteção de lotes enviados. Cães, estacionamento e estresse de Gimbalina já afetam a cena. O tráfego usa o grafo do quarteirão de treino. Faltam contato com carros em circulação, motoristas erráticos na via e sua retirada pela blitz. | [Facções e custódia](../docs/city-hazards.md) · [Encontros cotidianos](../docs/city-everyday-encounters.md) · [Adaptador de tráfego](../src/Gsa.Engine/Traffic/TrafficBenchAdapter.cs) |
| Desempenho | O benchmark de CPU já mediu uma queda de 111,256 para 9,070 ms na mediana de uma consulta com cache restrito, com os mesmos registros em ordem. A taxa de quadros da cidade a 1080p na GPU de referência continua por medir. | [Medições de LiDAR](../artifacts/evidence/performance-lidar-cpu/README.md) · [Evidência de M1](../artifacts/evidence/T-F19-01/README.md) |

As listas das funcionalidades registram 81 de 105 tarefas com aceite técnico. São 80 de 80 em M0–M6 e uma de dez em M7, a migração/compatibilidade de saves (T-F12-04). Das 24 tarefas ainda abertas, nove pertencem a M7, sete a M8/M9 e oito às expansões E1/E2. Os relatórios de M2 e M3 registram o circuito técnico e deixam aberto o aceite com jogadores. [Gate M2](../artifacts/evidence/T-F19-02/m2-gate-report.json) · [Gate M3](../artifacts/evidence/T-F19-03/m3-gate-report.json).

### Caso reproduzido em HIST-06

**O Túnel** pede uma nuvem em um trecho de 200 m no Porto. Em um slot temporário na cena Godot, um teste preparou os pré-requisitos até HIST-05 e uma Kombi. A entrega vazia manteve HIST-06 ativo e o saldo de 100 Coords. Com o LiDAR desligado, o personagem desembarcou da Kombi estacionada em E 500754 m, N 7401740 m, fotografou o único alvo solicitado pela cena (`survey_control_port_tunnel`) e usou a ação de processar/entregar. Uma foto aprovada, zero lotes enviados e zero pontos exibidos encerraram o contrato com saldo de 4.500 Coords. O teste acionou os callbacks de captura e entrega do jogo em modo headless e posicionou o veículo para isolar o critério de pagamento. [Objetivo de HIST-06](features/F10-missoes/spec.md) · [Seleção do produto e dos alvos](https://github.com/kauevestena/gsa/blob/d0f49e088e9c5efeb05b36187cb0cb6dd3b35228/src/Gsa.Engine/World/SaoDatumTablet.cs#L231-L267).

### Prioridades de desenvolvimento

1. **Corrigir a entrega na Carreira.** O primeiro PR deve ligar a entrega a uma revisão processada dos lotes protegidos, ao CQ do produto solicitado e à liquidação pelo livro-caixa. Começar por um contrato de fotos. Mostrar no quadro quais produtos já admitem coleta e deixar trabalhos antigos recuperáveis ou abandonáveis. No teste da cena exportada, repetir o caso de HIST-06 com uma foto deve manter o saldo e os desbloqueios. Um contrato fotográfico válido deve pagar conforme o laudo, uma única vez mesmo após salvar e recarregar. [CQ](features/F06-contratos-cq/spec.md) · [Liquidação existente](../src/Gsa.Missions/Contracts/ContractSettlementCoordinator.cs).
2. **Integrar os riscos de trânsito no Centro Velho.** Primeiro ligar uma rota urbana aos veículos já desenhados e resolver um impacto por contato com o jogador, usando a variação de velocidade e a regra de descalibração. Depois ligar o modelo de motoristas erráticos a esses mesmos agentes e à blitz. Na rota de teste, os avisos devem anteceder o encontro e a blitz deve retirar o agente. Ativar Cidade calma ou um contrato tranquilo durante o aviso deve cancelar o risco hostil e manter a circulação regular. Verificar cinco minutos de circulação com 50 carros e saída livre do escritório. [F09](features/F09-trafego-clima/spec.md) · [Parâmetros de tráfego](parametros.md).
3. **Validar o MVP em um save novo.** Levar HIST-01/02/03 aos objetivos de 20 fachadas, 12 bocas de lobo e oito segmentos de calçada. Percorrer os cinco templates procedurais com frota compatível e ao menos uma opção tranquila. Registrar uma coleta reprovada, a correção pedida no tablet, a entrega e o desbloqueio da plataforma seguinte após recarregar. Usar esse percurso para a revisão de controle, PT-BR/EN-US e escala de UI a 150%. [Missões e critérios](features/F10-missoes/spec.md) · [Interface](features/F11-interface-localizacao/spec.md).
4. **Entregar a primeira nuvem científica no túnel.** Ligar aquisição LiDAR, trajetória estimada e calibração da Kombi ao armazenamento dos lotes. O escritório deve informar cobertura, densidade e resíduos em pontos de checagem independentes, indicar a área de recoleta e avaliar novamente após a segunda passada. O mesmo lote deve produzir o mesmo laudo após recarga do save ou alteração do nível de detalhe visual. Reusar esse caminho para o perfil de pavimento e depois para HIST-10/11. Em HIST-11, validar as quatro regiões, o save entre atos, a variante tranquila e os dois prazos de entrega de 24 h recomendadas e 30 h máximas. [LiDAR](features/F15-lidar/spec.md) · [CQ de nuvem](../src/Gsa.Missions/Quality/PointCloudAndPavementQualityEvaluator.cs) · [Campanha](features/F10-missoes/spec.md).

**Desempenho.** Registrar a linha de base antes de alterar o tráfego e repetir o mesmo percurso e seed depois da integração. O protocolo pede três execuções de cinco minutos após 30 s de aquecimento, dia seco e noite chuvosa, a 1080p em GPU classe Vega 7. A bancada usa 50 carros e 30 pedestres. Medir CPU/GPU, fila de sensores, perda de amostras e evolução da memória na segunda metade de cada execução. As metas são média ≥60 fps, p95 ≤20 ms, p99 ≤33,3 ms, RAM ≤2 GiB, cache de nuvem ≤256 MiB e zero perda silenciosa. [Protocolo de benchmark](validacao.md#benchmark-de-desempenho).

**Playtests.** M2 pede cinco novatos, com pelo menos quatro concluindo em até 30 minutos com até duas intervenções, quatro identificando causa/correção e três escolhendo outro contrato. M3 pede cinco jogadores e cinco estudantes, com pelo menos quatro de cada grupo concluindo e corrigindo a coleta e três querendo continuar. Registrar também se os estudantes relacionam dois efeitos da simulação às medições. Arte final, dublagem e preparação da versão 1.0 seguem os marcos abaixo. [Protocolo completo](validacao.md) · [Pendências externas](decisoes.md).

## Lista executável

### M0 — Fundação

Estado de implementação: M0 integrado com aceite técnico delegado no [PR #1](https://github.com/kauevestena/gsa/pull/1), após CI nativa Linux/Windows. Ver [registro](../artifacts/evidence/M0-ci/summary.json). Inspeção gráfica e isolamento de rede Windows continuam separados; não bloqueiam o desenvolvimento contínuo autorizado.

**Gate de saída:** Projetos, ferramentas, schemas e localização disponíveis. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F01-01 | Fixar ferramentas e bootstrap | [Tarefas F01](features/F01-fundacao/tasks.md) |
| T-F01-02 | Criar módulos e testes mínimos | [Tarefas F01](features/F01-fundacao/tasks.md) |
| T-F01-03 | Implementar validação de conteúdo | [Tarefas F01](features/F01-fundacao/tasks.md) |
| T-F01-04 | Automatizar builds e exports | [Tarefas F01](features/F01-fundacao/tasks.md) |
| T-F06-01 | Definir e validar dados de contrato | [Tarefas F06](features/F06-contratos-cq/tasks.md) |
| T-F11-01 | Criar localização e glossário inicial | [Tarefas F11](features/F11-interface-localizacao/tasks.md) |

### M1 — Viabilidade técnica

Implementação M1 concluída e aceita por revisão técnica delegada até o PR #5 (`T-F19-01`), com evidências em [CI Wave 4](../artifacts/evidence/M1-wave4-ci/summary.json) e [relatório de benchmark](../artifacts/evidence/T-F19-01/README.md). O gate final de desempenho segue marcado como `pending_reference_hardware` aguardando execução não-headless na GPU de referência Radeon Vega 7.

**Gate de saída:** Bancada, orçamento de raios, 50 carros e relatório de hardware. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F02-01 | Criar graybox semântico e coordenadas | [Tarefas F02](features/F02-mundo/tasks.md) |
| T-F02-02 | Pré-calcular visibilidade de céu | [Tarefas F02](features/F02-mundo/tasks.md) |
| T-F03-01 | Implementar locomoção inicial e entradas | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F03-02 | Integrar galinha e estresse | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F04-01 | Implementar projeção e fixtures | [Tarefas F04](features/F04-cameras/tasks.md) |
| T-F04-02 | Integrar observações e miniaturas | [Tarefas F04](features/F04-cameras/tasks.md) |
| T-F05-01 | Implementar observações GNSS e heading | [Tarefas F05](features/F05-gnss/tasks.md) |
| T-F09-01 | Implementar tráfego regular de bancada | [Tarefas F09](features/F09-trafego-clima/tasks.md) |
| T-F15-01 | Construir bancada LiDAR | [Tarefas F15](features/F15-lidar/tasks.md) |
| T-F17-01 | Produzir kit piloto e pipeline de importação | [Tarefas F17](features/F17-arte-audio/tasks.md) |
| T-F19-01 | Instrumentar e medir protótipo | [Tarefas F19](features/F19-validacao-lancamento/tasks.md) |

### M2 — Primeira experiência

**Gate de saída:** Um contrato completo e playtest do loop aceito. Consultar o protocolo em [validação](validacao.md). Loop técnico completo verificado e vinculado às 15 tarefas de M2 no [relatório do gate M2](../artifacts/evidence/T-F19-02/m2-gate-report.json) (`technical_loop_status: "passed"`); fechamento final de playtest permanece em `pending_human_playtest_cohort` até execução com a coorte presencial de 5 novatos sem geomática prévia (`<= 30 min`).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F04-03 | Implementar scheduler e inventário | [Tarefas F04](features/F04-cameras/tasks.md) |
| T-F05-02 | Integrar céu e estado de solução | [Tarefas F05](features/F05-gnss/tasks.md) |
| T-F06-02 | Implementar máquina de contratos | [Tarefas F06](features/F06-contratos-cq/tasks.md) |
| T-F06-03 | Implementar CQ de fotos e inventário | [Tarefas F06](features/F06-contratos-cq/tasks.md) |
| T-F06-04 | Implementar estrelas e valor de entrega | [Tarefas F06](features/F06-contratos-cq/tasks.md) |
| T-F06-05 | Integrar retrabalho e liquidação idempotente | [Tarefas F06](features/F06-contratos-cq/tasks.md) |
| T-F07-01 | Criar processamento básico por receita | [Tarefas F07](features/F07-escritorio/tasks.md) |
| T-F07-04 | Implementar máscaras e conformidade | [Tarefas F07](features/F07-escritorio/tasks.md) |
| T-F08-01 | Implementar ledger e pagamentos | [Tarefas F08](features/F08-economia/tasks.md) |
| T-F10-01 | Criar tutorial e catálogo inicial de história | [Tarefas F10](features/F10-missoes/tasks.md) |
| T-F11-02 | Implementar HUD, mapa e ajustes | [Tarefas F11](features/F11-interface-localizacao/tasks.md) |
| T-F11-03 | Criar quadro, escritório e relatório | [Tarefas F11](features/F11-interface-localizacao/tasks.md) |
| T-F12-01 | Implementar lotes e descarregamento atômico | [Tarefas F12](features/F12-persistencia/tasks.md) |
| T-F12-02 | Implementar save e recuperação | [Tarefas F12](features/F12-persistencia/tasks.md) |
| T-F19-02 | Validar primeira experiência completa | [Tarefas F19](features/F19-validacao-lancamento/tasks.md) |

### M3 — MVP

**Gate de saída:** Níveis 0–2, cinco templates, três missões e gate dos dois públicos. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F02-03 | Montar Centro Velho do MVP | [Tarefas F02](features/F02-mundo/tasks.md) |
| T-F03-03 | Adicionar bicicleta e recursos | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F03-04 | Adicionar motoneta e recuperação | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F04-04 | Adicionar ambiente e sobreposição | [Tarefas F04](features/F04-cameras/tasks.md) |
| T-F04-05 | Integrar capacete e câmeras frente/trás | [Tarefas F04](features/F04-cameras/tasks.md) |
| T-F05-03 | Adicionar aluguel e DGNSS | [Tarefas F05](features/F05-gnss/tasks.md) |
| T-F07-02 | Integrar Arcade e Técnico simplificados | [Tarefas F07](features/F07-escritorio/tasks.md) |
| T-F08-02 | Adicionar loja e progressão inicial | [Tarefas F08](features/F08-economia/tasks.md) |
| T-F09-02 | Implementar calendário, clima e aderência | [Tarefas F09](features/F09-trafego-clima/tasks.md) |
| T-F09-03 | Adicionar bêbados, blitz e cães | [Tarefas F09](features/F09-trafego-clima/tasks.md) |
| T-F09-04 | Implementar contratos tranquilos e Cidade calma | [Tarefas F09](features/F09-trafego-clima/tasks.md) |
| T-F10-02 | Completar MVP e gerador de cinco templates | [Tarefas F10](features/F10-missoes/tasks.md) |
| T-F11-04 | Entregar Geopédia e revisão do MVP | [Tarefas F11](features/F11-interface-localizacao/tasks.md) |
| T-F12-03 | Implementar replay e ferramenta de relatório | [Tarefas F12](features/F12-persistencia/tasks.md) |
| T-F18-01 | Adicionar ovos, marcos e cartas | [Tarefas F18](features/F18-atividades/tasks.md) |
| T-F19-03 | Validar MVP com os dois públicos | [Tarefas F19](features/F19-validacao-lancamento/tasks.md) |

### M4 — Plataforma automotiva

**Gate de saída:** Carro, 360°, rack/oficina, RTK/MEMS, facções iniciais e roteiro completo. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F02-04 | Expandir Centro, Porto e Orla | [Tarefas F02](features/F02-mundo/tasks.md) |
| T-F03-05 | Implementar carro com suspensão | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F04-06 | Adicionar câmera 360° | [Tarefas F04](features/F04-cameras/tasks.md) |
| T-F05-04 | Adicionar RTK e instalação de base | [Tarefas F05](features/F05-gnss/tasks.md) |
| T-F08-03 | Implementar habilidades e reputação completa | [Tarefas F08](features/F08-economia/tasks.md) |
| T-F08-04 | Integrar serviços, seguro e frota avançada | [Tarefas F08](features/F08-economia/tasks.md) |
| T-F09-05 | Integrar perigos e contramedidas urbanas | [Tarefas F09](features/F09-trafego-clima/tasks.md) |
| T-F10-03 | Concluir ato 1 e escrever campanha restante | [Tarefas F10](features/F10-missoes/tasks.md) |
| T-F10-06 | Estabilizar roteiros para gravação | [Tarefas F10](features/F10-missoes/tasks.md) |
| T-F13-01 | Implementar rack e revisões de montagem | [Tarefas F13](features/F13-calibracao/tasks.md) |
| T-F13-02 | Integrar descalibração por impacto | [Tarefas F13](features/F13-calibracao/tasks.md) |
| T-F13-03 | Criar minigames e solvers | [Tarefas F13](features/F13-calibracao/tasks.md) |
| T-F14-01 | Implementar IMU e atitude | [Tarefas F14](features/F14-fusao/tasks.md) |
| T-F16-01 | Criar territórios e identidades fictícias | [Tarefas F16](features/F16-faccoes/tasks.md) |
| T-F16-02 | Implementar atenção e ameaças iniciais | [Tarefas F16](features/F16-faccoes/tasks.md) |
| T-F18-02 | Adicionar álbum e cosméticos | [Tarefas F18](features/F18-atividades/tasks.md) |

### M5 — Levantamento avançado

**Gate de saída:** Kombi, LiDAR, DMI/ZUPT, PPK, CQ avançado e proteção de dados. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F03-06 | Adicionar Kombi | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F06-06 | Adicionar CQ de nuvem e pavimento | [Tarefas F06](features/F06-contratos-cq/tasks.md) |
| T-F07-03 | Integrar processamento avançado | [Tarefas F07](features/F07-escritorio/tasks.md) |
| T-F07-05 | Consolidar jobs e caches | [Tarefas F07](features/F07-escritorio/tasks.md) |
| T-F13-04 | Integrar oficina às passadas LiDAR | [Tarefas F13](features/F13-calibracao/tasks.md) |
| T-F14-02 | Implementar DMI e ZUPT | [Tarefas F14](features/F14-fusao/tasks.md) |
| T-F14-03 | Integrar filtro de trajetória | [Tarefas F14](features/F14-fusao/tasks.md) |
| T-F14-04 | Adicionar PPK, controles e heading duplo | [Tarefas F14](features/F14-fusao/tasks.md) |
| T-F15-02 | Integrar perfilador e georreferenciamento | [Tarefas F15](features/F15-lidar/tasks.md) |
| T-F15-03 | Implementar octree, chunks e LOD | [Tarefas F15](features/F15-lidar/tasks.md) |
| T-F16-03 | Adicionar sabotagem avançada e contramedidas | [Tarefas F16](features/F16-faccoes/tasks.md) |

### M6 — Terrenos e ato 2

**Gate de saída:** Picape, Serra, multirretorno, eventos e ato 2 concluível. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F02-05 | Produzir Serra e terrenos especiais | [Tarefas F02](features/F02-mundo/tasks.md) |
| T-F03-07 | Adicionar picape 4x4 | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F10-04 | Implementar ato 2 e templates avançados | [Tarefas F10](features/F10-missoes/tasks.md) |
| T-F15-04 | Adicionar sensores pareados e múltiplos retornos | [Tarefas F15](features/F15-lidar/tasks.md) |
| T-F18-03 | Adicionar eventos absurdos | [Tarefas F18](features/F18-atividades/tasks.md) |

### M7 — Campanha completa

**Gate de saída:** Van, quatro regiões, ato 3, Geopédia avançada e benchmark. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F02-06 | Integrar cidade e rede de marcos | [Tarefas F02](features/F02-mundo/tasks.md) |
| T-F03-08 | Adicionar van MMS | [Tarefas F03](features/F03-mobilidade/tasks.md) |
| T-F04-07 | Implementar produto de pavimento | [Tarefas F04](features/F04-cameras/tasks.md) |
| T-F10-05 | Implementar ato 3 e levantamento final | [Tarefas F10](features/F10-missoes/tasks.md) |
| T-F11-05 | Completar UI técnica e Geopédia avançada | [Tarefas F11](features/F11-interface-localizacao/tasks.md) |
| T-F12-04 | Adicionar migração e compatibilidade | [Tarefas F12](features/F12-persistencia/tasks.md) |
| T-F15-05 | Entregar perfil de engenharia e otimizar aquisição | [Tarefas F15](features/F15-lidar/tasks.md) |
| T-F16-04 | Integrar facções à cidade completa | [Tarefas F16](features/F16-faccoes/tasks.md) |
| T-F17-02 | Produzir arte da cidade e frota completas | [Tarefas F17](features/F17-arte-audio/tasks.md) |
| T-F19-04 | Otimizar e medir cidade completa | [Tarefas F19](features/F19-validacao-lancamento/tasks.md) |

### M8 — Conteúdo final

**Gate de saída:** Dublagem, rádios, cutscenes, atividades e revisão bilíngue. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F11-06 | Revisar localização e acessibilidade final | [Tarefas F11](features/F11-interface-localizacao/tasks.md) |
| T-F17-03 | Produzir e integrar dublagem bilíngue | [Tarefas F17](features/F17-arte-audio/tasks.md) |
| T-F17-04 | Produzir rádios, trilha e mixagem | [Tarefas F17](features/F17-arte-audio/tasks.md) |
| T-F17-05 | Integrar cutscenes e apresentação final | [Tarefas F17](features/F17-arte-audio/tasks.md) |
| T-F18-04 | Entregar desafio, filtros e coleção completa | [Tarefas F18](features/F18-atividades/tasks.md) |

### M9 — Versão 1.0

**Gate de saída:** RC validado e pacote de publicação com decisões externas resolvidas. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F19-05 | Produzir e validar release candidate | [Tarefas F19](features/F19-validacao-lancamento/tasks.md) |
| T-F19-06 | Preparar distribuição e resolver gates externos | [Tarefas F19](features/F19-validacao-lancamento/tasks.md) |

### E1 — Exportações

**Gate de saída:** LAS/LAZ, CSV e GeoJSON verificados em leitores externos. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F20-01 | Definir adaptadores e dependências de exportação | [Tarefas F20](features/F20-exportacao/tasks.md) |
| T-F20-02 | Implementar LAS e LAZ | [Tarefas F20](features/F20-exportacao/tasks.md) |
| T-F20-03 | Implementar CSV e GeoJSON sintético | [Tarefas F20](features/F20-exportacao/tasks.md) |
| T-F20-04 | Validar exportação em ferramentas externas | [Tarefas F20](features/F20-exportacao/tasks.md) |

### E2 — Plataformas especiais

**Gate de saída:** Mochila, rodoferroviário e barco com missões e produção completa. Consultar o protocolo em [validação](validacao.md).

| Tarefa | Incremento | Documento |
| --- | --- | --- |
| T-F21-01 | Implementar mochila e levantamento interior | [Tarefas F21](features/F21-plataformas-especiais/tasks.md) |
| T-F21-02 | Implementar veículo rodoferroviário | [Tarefas F21](features/F21-plataformas-especiais/tasks.md) |
| T-F21-03 | Implementar barco e margens | [Tarefas F21](features/F21-plataformas-especiais/tasks.md) |
| T-F21-04 | Entregar missões e produção da expansão | [Tarefas F21](features/F21-plataformas-especiais/tasks.md) |

## Passagem de marco

Responsável técnico consolida testes/performance; direção de produto revisa jogabilidade; revisão técnica pedagógica verifica modelos e explicações; produção revisa arte/idiomas/voz. Uma pessoa pode exercer mais de um papel, mas deve registrar qual aceite realizou. Gate falho gera tarefa de correção ligada ao requisito e nova evidência; não marcar automaticamente aceito.

M9 separa pacote tecnicamente validado de publicação efetiva. EXT-02/03/04/05/06 afetam o pacote público; EXT-07 afeta aceite de desempenho. Exportações/plataformas especiais podem ser desenvolvidas localmente a partir do RC tecnicamente aceito enquanto publicação externa aguarda autorização, desde que o gate técnico de M9 esteja registrado; isso não autoriza distribuí-las.
