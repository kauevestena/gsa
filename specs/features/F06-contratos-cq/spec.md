# F06 — Contratos, qualidade e liquidação

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M0–M5. Origem: Loop de jogo e estrutura; Núcleo de simulação MMS; Missões. Estado: schema/validação T-F06-01 integrado com aceite técnico delegado; estados, CQ e liquidação dos marcos seguintes pendentes.

## Requisitos

### REQ-F06-01 — Contrato explícito

Quadro deve apresentar cliente, área, produto, exigências, prazo recomendado/final, pagamento, multa, risco e tranquilidade antes de aceitar.

### REQ-F06-02 — Estado e custódia

Contrato segue a máquina de estados, permite retrabalho e usa somente lotes descarregados para entregar; destruição/fuga preserva dados protegidos.

### REQ-F06-03 — CQ mensurável

Calcular cobertura, completude, densidade, precisão absoluta/relativa, imagem e conformidade conforme produto, sem compensar reprovação por estrelas.

### REQ-F06-04 — Pagamento coerente

Liquidar uma única vez por instância; parcial apenas por entregáveis previstos; aplicar qualidade, atraso e expiração explicitamente.

### REQ-F06-05 — Diagnóstico acionável

Relatório mostra métrica, unidade, limite, valor, causa provável e áreas para recoleta, com apresentação Arcade/Técnico.

## Comportamento e limites

Aceitar fixa snapshot das exigências, prazo e versão de conteúdo; alterações de balanceamento não mudam contrato já aceito. Contratos oferecem produto interno `gsa_bundle_v1`, mesmo sem exportação externa. Formato é validado pelo manifesto e conteúdo, não por extensão de arquivo.

Somente métricas aplicáveis ao produto são exigidas. Inventário sem três checkpoints quando RMSE é obrigatório é insuficiente; nuvem vazia nunca passa densidade. Para completude de 100%, usar predicado binário; fórmulas quantitativas exigem alvo de excelência distinto.

Descarregar e entregar são ações diferentes. Descarregar protege dados; entregar encerra contrato e aciona ledger. Pagamento parcial oferece lista/valor dos entregáveis aceitos e encerra a instância quando o jogador o escolhe. Privacidade e formato são invariantes globais, inclusive em parcial. Recoletar permite unir amostras únicas, não somar a mesma amostra duas vezes.

CQ final referencia pontos/superfícies de avaliação da revisão da coleta. Objetos dinâmicos/marcos movidos não podem alterar retroativamente o oracle de uma aquisição. Relatório deve ser invalidado se o produto mudar após sua avaliação.

## Interfaces

ContractDefinition/Instance, QualityReport, EvaluationReference, ProcessedProduct e evento de liquidação. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F06-01

Dado um contrato com uma exigência reprovada, quando avaliar, então entrega integral é bloqueada mesmo que outras métricas sejam excelentes e o relatório indica correção.

### AC-F06-02

Dados lotes repetidos ou pagamento repetido após save, quando executar o comando, então cobertura e saldo não são duplicados.

### AC-F06-03

Dada entrega após due mas antes de expires e depois de expires, então a primeira aplica multa calculada e a segunda encerra sem pagamento.

### AC-F06-04

Dada coleta com parte protegida e parte perdida, quando recoletar, então é possível completar os dados remanescentes até expiração.

### AC-F06-05

Dado fixture de células recortadas/checkpoints/nuvem, quando calcular CQ, então os valores correspondem ao cálculo independente.
