# F03 — Plataformas, condução e Gimbalina

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1–M7. Origem: Progressão de plataformas e sensores; Fun elements; Perigos e antagonistas. Estado: especificado; não implementado.

## Requisitos

### REQ-F03-01 — Condução progressiva

Implementar sete plataformas com capacidades, velocidades e desbloqueios da tabela, cada uma introduzindo a mecânica prevista.

### REQ-F03-02 — Galinha estabilizadora

Simular estabilização, estresse, repouso, milho, fuga segura e aposentadoria como mascote sem ferimentos.

### REQ-F03-03 — Impactos e recuperação

Colisões produzem dano, eventos de rack e ragdoll cartunesco; recuperação respeita custódia dos dados e evita bloqueio econômico.

### REQ-F03-04 — Recursos e controles

Consumo de energia/combustível/milho e armazenamento limitam captura; teclado/gamepad são remapeáveis e funcionam em todas as plataformas.

## Comportamento e limites

Carrinho privilegia calçadões; bicicleta acessa becos/ciclovias; motoneta introduz rua e câmeras frente/trás. Carro exige controle de montagem e tráfego completo. Kombi permite operação LiDAR/túnel; picape vence terra/encosta; van oferece sensores de engenharia e pavimento. Não disponibilizar slots de LiDAR antes do nível 4.

O jogador anda a pé a 3 m/s para ocupar marco, recuperar Gimbalina, limpar lente e interagir; não há combate. Entrar/sair exige veículo parado abaixo de 0,1 m/s e área livre. Missões não exigem roubar veículos de NPCs.

Cabeça de Gimbalina é estabilizada visualmente por contrarrotação limitada a 45°; componente residual/estresse alimenta a câmera de verdade. Corpo e protagonista podem usar ragdoll visual, mas o estado autoritativo de veículo/dado é preservado. Em fuga ou destruição, fade/reposicionamento seguro no escritório mantém acessibilidade; sem sangue ou galinha danificada.

Níveis 0–2 usam controladores cinemáticos com resposta de aderência e obstáculos; níveis 3–6 usam suspensão por raios, tração e frenagem em corpo rígido. Não implementar transmissão realista como pré-requisito. Recursos e condição pertencem ao estado persistente, não à cena visual.

## Interfaces

VehicleState, VehicleProfile, CaptureBatch custódia, eventos de impacto/estresse e comandos de entrada. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F03-01

Dada cada plataforma, quando dirigir por sua rota de referência, então controles funcionam, velocidade respeita perfil e a nova mecânica é exercitável.

### AC-F03-02

Dada galinha calma e depois estressada, quando percorrer o mesmo trecho, então atitude/arrasto e aparência mudam causalmente; em fuga ela reaparece segura.

### AC-F03-03

Dado veículo com lotes pendentes e protegidos, quando destruí-lo, então apenas pendentes se perdem e existe recuperação jogável.

### AC-F03-04

Dado recurso esgotado ou controle remapeado, quando coletar/dirigir, então captura para com causa clara e a ação remapeada funciona.
