# F09 — Tráfego, clima e perigos cotidianos

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1–M4. Origem: Loop de jogo e estrutura; Perigos e antagonistas; Missões. Estado: domínio implementado; cães/estacionamento integrados à cena com testes automatizados. Tráfego errático e playtest visual da cidade pendentes.

## Requisitos

### REQ-F09-01 — Tráfego e pedestres

Simular vias/faixas, semáforos, cruzamentos, pedestres, horários e 50 carros no benchmark com sinais observáveis de risco.

### REQ-F09-02 — Tempo e clima

Dia/noite, seco/chuva afetam iluminação, sensores, aderência e densidade de tráfego; calendário é persistente.

### REQ-F09-03 — Direção errática

Motoristas bêbados têm ruído de direção/velocidade, infrações e sinais visíveis, com frequência contextual e blitz que os retira de circulação.

### REQ-F09-04 — Perigos cotidianos

Buracos, buzinas, cães, fogos, flanelinhas, tinta e colisões geram efeitos definidos no veículo/galinha/dados e contramedidas.

### REQ-F09-05 — Cidade calma

Contratos tranquilos e ajuste Cidade calma suprimem perigos previstos, mantendo aprendizagem, recompensas e conclusão da campanha.

## Comportamento e limites

Rede viária possui nós de faixa, prioridade, limite, travessias e restrições horárias. Agentes seguem caminho local e frenagem para evitar colisão regular; congestionamento não pode bloquear permanentemente saída do escritório. Após 20 s sem progresso e fora da câmera, agente pode ser removido/reposto, preservando eventos relevantes.

Bêbados usam os mesmos controladores com perturbação correlacionada de volante (amplitude inicial 0,2 rad, período 2–5 s), velocidade ±30% e chance de infração de 20% por cruzamento; ziguezague, som e farol denunciam risco. Blitz é evento visível que remove agentes erráticos ao passarem pelo controle. Polícia só aplica multas de trânsito ao jogador.

Cão persegue bicicleta por até 10 s em raio de 30 m, desistindo com distância >40 m ou petisco. Flanelinha oferece serviço de estacionamento e, em recusa, pode causar dano cosmético de 5 pontos ao veículo, sem perda de dado direta; Cidade calma remove retaliação. Tinta/tomate obstrui lente até limpeza parado por 3 s. Buracos/lombadas aplicam impacto conforme velocidade e podem descalibrar após M4.

Contratos tranquilos reservam sua área e rotas de entrada/saída sem spawn de agentes hostis enquanto ativos; perigos preexistentes são retirados com transição fora de vista. Trocar Cidade calma limpa perseguições e desarma bombas em andamento, preservando dados. Um indicador mostra que a proteção está ativa.

## Interfaces

TrafficAgentState, WeatherState, CalendarState, HazardEvent e CalmPolicy; eventos alimentam sensores/calibração/carreira. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F09-01

Dado cenário com 50 carros, quando rodar cinco minutos, então circulação continua sem impasse permanente e o benchmark registra custo do subsistema.

### AC-F09-02

Dado mesmo percurso seco/chuva e dia/noite, então aderência e observações mudam conforme parâmetros e o calendário recarrega corretamente.

### AC-F09-03

Dado agente errático, então seus sinais antecedem a ameaça e a blitz o remove sem premiar o jogador por atropelar/atacar.

### AC-F09-04

Dado contrato tranquilo ou Cidade calma ativada durante perseguição, então perigos são suprimidos e nenhum evento pendente causa dano posterior.

## Integração jogável incremental — 20/11/2026

A [matriz de cães/estacionamento](../../../docs/city-everyday-encounters.md) fixa aviso de 2 s e frenagem de 2 s no contato, raio de estacionamento de 8 m, aviso de recusa de 3 s e cooldown de 30 s. São parâmetros iniciais de apresentação, mantendo 30/40 m e 10 s do cão, tarifa de 10 e dano cosmético de 5. A oscilação existente de Gimbalina passa à exposição da câmera; arranhões não afetam dados. Testes antigos de domínio não comprovam que tráfego errático/colisões já estejam ligados à cidade.
