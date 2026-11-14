# Plano — F08

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar ledger e idempotência antes de ligar pagamento e loja. Adicionar árvore de capacidades e desbloqueios orientados a dados. Custos são políticas de serviço com a mesma API de transação; seguro/recuperação usa eventos de perda de veículo e não manipula lotes protegidos.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F08-01 — Implementar ledger e pagamentos | M2 | T-F06-04 |
| T-F08-02 — Adicionar loja e progressão inicial | M3 | T-F08-01, T-F10-01 |
| T-F08-03 — Implementar habilidades e reputação completa | M4 | T-F08-02 |
| T-F08-04 — Integrar serviços, seguro e frota avançada | M4 | T-F08-02 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
