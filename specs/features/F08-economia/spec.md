# F08 — Economia, carreira e frota

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M2–M4. Origem: Loop de jogo e estrutura; Progressão de plataformas e sensores. Estado: especificado; não implementado.

## Requisitos

### REQ-F08-01 — Livro-caixa

Registrar receitas/custos em Coords inteiros, com eventos idempotentes e saldo consistente com save.

### REQ-F08-02 — Progressão

Liberar plataformas por história, reputação e compra; preservar frota anterior e oferecer seis trilhas de três postos.

### REQ-F08-03 — Serviços e custos

Cobrar combustível/milho, manutenção, seguro, base, armazenamento, processamento, multas e serviços de proteção conforme parâmetros.

### REQ-F08-04 — Sem bloqueio econômico

Garantir recuperação gratuita do kit inicial e contratos básicos acessíveis mesmo sem saldo, sem alterar a qualidade exigida.

## Comportamento e limites

Ledger é a fonte do saldo, com ID único por transação e referência ao evento causador. Compras debitam somente depois de validar saldo/capacidade; transação e inventário são persistidos juntos. Valores monetários usam inteiros; arredondar cada liquidação uma vez no final, conforme modelos.

Progressão não compra qualidade fictícia. Habilidades oferecem tolerância física, avisos, assistência, tempo ou negociação, mas não alteram métricas medidas. Reputação é separada por cliente/bairro; global evita dupla contagem. Loja lista requisitos não atendidos e a mecânica nova de cada plataforma.

Os sensores disponíveis seguem nível e capacidade do rack. Preço inicial do conjunto de plataforma inclui seu perfil básico; melhorias individuais custam 20% do preço da plataforma por grau, até o grau permitido naquele nível. Não é possível comprar LiDAR e montar em carrinho. Seguro recompõe equipamento após franquia, sem restaurar coleta pendente. Armazenamento cobrado é serviço fictício; não depende de nuvem real.

Campanha deve continuar mesmo com dívida de multas: kit inicial, contratos tranquilos e assistência mínima permitem recuperar saldo. Não oferecer recompensa por direção alcoolizada, ferimentos ou ataques.

## Interfaces

LedgerEntry, CareerState, ReputationState, FleetInventory e eventos de entrega/compra/serviço. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F08-01

Dado evento de receita/custo repetido ou save recarregado, então saldo e inventário refletem a transação exatamente uma vez.

### AC-F08-02

Dado progresso insuficiente e depois suficiente, quando comprar plataforma/habilidade, então pré-requisitos e consumo de saldo/pontos são corretos.

### AC-F08-03

Dado veículo segurado destruído, então reposição exige franquia e não recupera dados pendentes.

### AC-F08-04

Dado saldo zero/dívida, então o jogador recupera kit inicial e conclui contrato tranquilo sem empréstimo obrigatório.
