# Plano — F07

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Construir processamento como operações puras encadeadas por receita, com validação de entradas. Adapter executa jobs em workers sobre dados imutáveis e entrega progresso ao escritório. Cada estágio tem hash de parâmetros/entradas para reuso; alterar calibração/base invalida os estágios dependentes. Máscaras e descarte de imagens entram antes de CQ.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F07-01 — Criar processamento básico por receita | M2 | T-F04-03, T-F06-03 |
| T-F07-02 — Integrar Arcade e Técnico simplificados | M3 | T-F07-01, T-F11-02 |
| T-F07-03 — Integrar processamento avançado | M5 | T-F14-04, T-F13-03, T-F15-03 |
| T-F07-04 — Implementar máscaras e conformidade | M2 | T-F07-01, T-F04-03 |
| T-F07-05 — Consolidar jobs e caches | M5 | T-F07-03, T-F12-02 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
