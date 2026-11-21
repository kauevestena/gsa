# F10 — Campanha e geração de contratos

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M2–M7. Origem: Missões; Loop de jogo e estrutura; Mundo; Escopo, MVP e roadmap. Estado: especificado; não implementado.

## Requisitos

### REQ-F10-01 — Missões de história

Implementar três atos e as onze missões nomeadas, com três missões iniciais no MVP, objetivos, falas, desbloqueios e recuperação.

### REQ-F10-02 — Gerador factível

Gerar contratos de cliente/área/produto/especificação/prazo/clima/risco compatíveis com frota e rotas comprovadas.

### REQ-F10-03 — Variantes acessíveis

Cada missão perigosa deve ter variante Cidade calma com mesmo produto, pagamento e progressão, sem exigir negociação ou ataques.

### REQ-F10-04 — Roteiro executável

Roteiro de cada missão define condições, eventos, diálogos localizáveis, falha/retrabalho, cutscenes e referências de áudio.

## Comportamento e limites

Catálogo inicial normativo:

| ID | Ato / plataforma mínima | Missão / objetivo e desbloqueio |
| --- | --- | --- |
| HIST-01 | 1 / carrinho | Fachadas da Rua do Comércio: 20 fachadas; tutorial de captura/CQ |
| HIST-02 | 1 / carrinho | Bueiros Perdidos: 12 bocas de lobo antes da tempestade; libera compra da bicicleta |
| HIST-03 | 1 / bicicleta | Calçada Acessível: 8 segmentos de largura/degrau com controles fornecidos; libera motoneta |
| HIST-04 | 1 / motoneta | Placas da Avenida: 12 placas em tráfego; fecha ato e libera carro/Porto/Orla |
| HIST-05 | 2 / carro | Corredor de Ônibus: 360° com faixa exclusiva liberada 10–16h; libera Kombi por reputação |
| HIST-06 | 2 / Kombi | O Túnel: 200 m na entrada pelo Porto, ZUPT/controles nas bocas |
| HIST-07 | 2 / Kombi | Vistoria do Viaduto: 100 m sob tabuleiro; libera picape/Serra |
| HIST-08 | 2 / picape | Noite de Chuva: 300 m de avenida com chuva e filtragem |
| HIST-09 | 2 / carro | Grileiros no Retrovisor: seis divisas com marcos deslocados e perseguição; fecha ato |
| HIST-10 | 3 / Kombi | Obra Fantasma: perímetro de obra clandestina 100×100 m; descarregar sob ameaça; libera van |
| HIST-11 | 3 / van | O Grande Levantamento: rede viária/alvos exigidos nas quatro regiões em 24h recomendadas/30h finais |

História progride nessa ordem; mínimos de plataforma não obrigam abandonar veículo superior compatível. HIST-09 exige ao menos carro, embora seja enfrentada após desbloquear picape. HIST-11 cobre corredores/feições contratados de toda a cidade, não todos os interiores ou cada m² inacessível. Manifest final divide a coleta em quatro setores descarregáveis, com requisitos de engenharia.

HIST-02 usa chuva anunciada como obstáculo de qualidade, não falha instantânea independente do prazo. HIST-03 usa alvos/planos de controle locais para precisão relativa plausível com câmeras iniciais. O tutorial fornece essas referências e explica a diferença entre controle e checagem.

HIST-07 exige apresentar h e H dos pontos de controle do viaduto usando a ondulação geoidal fictícia; trocar as duas alturas deve reprovar a conformidade do campo de referência vertical, mesmo quando os pontos geométricos estão corretos.

A primeira integração jogável de HIST-05 usa inventário das seis paradas para exercitar faixa, panoramas, custódia, CQ e progressão. Os limites do [produto 360°](../../parametros.md#sensores-e-qualidade) seguem como alvo de aceite. Para encerrar a integração, avaliar cobertura contínua e RMSE ≤0,5 m com posicionamento corrigido ou controle; manter parada e controle independentes. Consulte os [limites da cena](../../../docs/city-hazards.md).

Templates MVP: PROC-FACHADAS (prefeitura/fotos), PROC-DRENAGEM (prefeitura/inventário), PROC-CALCADAS (universidade/inventário), PROC-PLACAS (empresa de mapas/inventário), PROC-PARQUE (seguradora/fotos tranquilas). Cada um possui pelo menos duas áreas/rotas variantes dentro do MVP. Amostrar apenas de combinações pré-validadas de área, clima e equipamento; seed escolhe subconjunto de feições, horário, cliente permitido e faixa de recompensa. Não resolver planejamento universal em runtime.

Depois de M5, adicionar PROC-ENERGIA (concessionária/inventário), PROC-OBRAS (construtora/nuvem), PROC-PAVIMENTO (prefeitura/perfil), PROC-TUNEL (universidade/nuvem). Gerador só oferece o que o inventário pode executar e o acesso permite; manter pelo menos um contrato tranquilo básico disponível.

Cada missão tem cinco beats mínimos: proposta, preparação, descoberta técnica, contratempo/correção, entrega/consequência. A história apresenta cada facção sem referência a grupos reais. Cutscenes são puláveis, com objetivos e desbloqueios aplicados por eventos idempotentes fora da reprodução audiovisual.

## Interfaces

MissionDefinition, ContractDefinition, CampaignState, SceneCue e DialogueEntry; eventos de objetivo/portais. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F10-01

Dado novo jogo, então HIST-01/02/03 são concluíveis com plataformas disponíveis e liberam bicicleta/motoneta sem dependência circular.

### AC-F10-02

Dados 100 seeds por template, então todos os contratos usam regiões acessíveis, frota compatível e prazo/recursos suficientes na rota testemunha.

### AC-F10-03

Dada Cidade calma, então todas as missões perigosas podem ser concluídas com os mesmos desbloqueios e produtos.

### AC-F10-04

Dada campanha completa com save entre atos e cutscenes puladas, então HIST-11 é alcançável e o fim desbloqueia o filtro de nuvem sem eventos duplicados.
