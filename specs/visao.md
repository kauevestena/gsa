# Visão, escopo e critérios de produto

## Objetivo

GSA é um jogo standalone e offline de levantamento móvel em São Datum. Elias Anacleto, recém-formado em Engenharia Cartográfica, sem emprego e sem dinheiro, herda da avó um carrinho de mão e Gimbalina e progride até uma van MMS. Mapas desatualizados sustentam grilagem, obras clandestinas e burocracia; o trabalho do protagonista contraria esses interesses. O dado aprovado pelo controle de qualidade é o resultado de cada contrato. Dirigir, planejar, calibrar e negociar substituem o combate pelo protagonista.

O produto atende jogadores por diversão e estudantes de Geomática/Agrimensura/Engenharia Cartográfica. Arcade e Técnico compartilham observações, erros e critérios de qualidade; mudam apresentação e assistência ao processamento. A simulação explica relações físicas e admite simplificações documentadas; não reivindica equivalência a equipamentos ou softwares profissionais.

## Decisões confirmadas

| Dimensão | Baseline |
| --- | --- |
| Produto | Jogo completo especificado; entrega incremental com revisão humana |
| Engine | Godot 4 .NET; núcleo C# independente; integração C# e cenas/scripts GDScript |
| Sistemas | Windows e Linux x86-64, instalação local, sem conta ou servidor |
| Campanha 1.0 | Três atos, sete níveis 0–6 e quatro regiões |
| Área | Cerca de 4 km² totais; MVP: 500 × 500 m do Centro Velho |
| Identidade | Elias Anacleto fixo; São Datum; Gimbalina; Coords como moeda inicial |
| Linguagem | PT-BR/EN-US completos; dublagem bilíngue na 1.0 |
| Conteúdo | Meta de design de 14 anos; classificação oficial é etapa de lançamento |
| Violência | Sem sangue, sem armas pelo jogador, acidentes cartunescos; galinha nunca ferida |
| Publicação | Gratuita, com intenção de código aberto; licença e autorização institucional pendentes |
| Expansões | LAS/LAZ, CSV, GeoJSON, mochila LiDAR, rodoferroviário e barco |

GSA é codinome interno. Não adotar o título expandido como nome público sem concluir a tarefa de identidade/publicação. Os nomes e as marcas do universo são fictícios; não associar facções a comunidades reais.

## Fronteiras por entrega

| Entrega | Inclui | Não integra sua versão jogável |
| --- | --- | --- |
| M1: bancada | Um quarteirão, carrinho/galinha, câmera, LiDAR experimental, cenário de 50 veículos | Campanha e promessa de LiDAR disponível no início do jogo |
| M2: fatia vertical | Contrato completo de fachadas com recoleta e pagamento; escritório; save; UI nos dois idiomas | Expansão do mundo |
| M3: MVP | Níveis 0–2; 0,25 km²; seco/chuva/dia/noite; cinco templates procedurais; três missões de história; tráfego errático e contratos tranquilos; Arcade/Técnico simplificados | LiDAR jogável, oficina de calibração, facções, demais regiões, rádios e cutscenes completas |
| M4–M8: jogo completo | Níveis 3–6, quatro regiões, campanha, processamento avançado, facções, Geopédia, conteúdo lateral, produção audiovisual bilíngue | Exportações e plataformas especiais |
| M9: 1.0 | Conteúdo completo, validação e pacotes distribuíveis | macOS, suporte específico a Steam Deck, multiplayer, serviços online |
| E1/E2 | Exportação científica e plataformas especiais com missões próprias | Conversão do jogo em ferramenta profissional certificada |

O MVP tem textos e interface completos nos dois idiomas; vozes provisórias ou ausência temporária de voz são aceitas até M8. A 1.0 exige todos os diálogos e programas falados dublados nos dois idiomas, inclusive o conteúdo do MVP.

## Sucesso

- Um jogador consegue explicar, após o tutorial, por que uma coleta falhou e corrigi-la pelas mecânicas.
- A melhora do produto decorre de observações, cobertura, planejamento ou processamento verificável, nunca de pontuação desvinculada dos dados.
- Cada plataforma introduz a mecânica especificada em sua progressão.
- O jogo permanece concluível em Cidade calma, sem penalidade oculta de qualidade ou progressão.
- A campanha é concluível offline nos dois sistemas, inclusive após salvar e recarregar.
- M2/M3 validam interesse e compreensão antes da produção intensiva do restante da cidade; ver [protocolo de playtest](validacao.md).

## Hipóteses de produção

Equipe e disponibilidade não foram definidas; o roadmap não promete datas. O prazo original de duas a três semanas para o protótipo é uma intenção histórica, não compromisso. Conteúdo, arte, gravação, tradução e revisão técnica têm tarefas próprias: agentes podem produzir implementações e rascunhos, mas não podem declarar concluída uma gravação ou revisão humana não realizada.
