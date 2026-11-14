# Plano — F06

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar schema/validação antes da UI e estados antes de integrar economia. Avaliadores são funções por métrica com suporte comum para célula/feição. Agregação de estrelas e pagamento utiliza modelos.md. Fase MVP avalia fotos/inventário; densidade e discrepância ponto-plano entram com LiDAR sem alterar a API de relatório.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F06-01 — Definir e validar dados de contrato | M0 | T-F01-02 |
| T-F06-02 — Implementar máquina de contratos | M2 | T-F06-01 |
| T-F06-03 — Implementar CQ de fotos e inventário | M2 | T-F06-01, T-F04-01 |
| T-F06-04 — Implementar estrelas e valor de entrega | M2 | T-F06-03 |
| T-F06-05 — Integrar retrabalho e liquidação idempotente | M2 | T-F06-02, T-F06-04, T-F12-01, T-F08-01 |
| T-F06-06 — Adicionar CQ de nuvem e pavimento | M5 | T-F06-03, T-F15-02 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
