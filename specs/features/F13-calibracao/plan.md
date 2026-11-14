# Plano — F13

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Separar catálogo de slots do solver. Primeiro aplicar extrínsecos estimados na coleta e adicionar impactos; depois oficina com datasets observáveis e held-out. No estágio LiDAR, reutilizar alinhamento ponto-plano com passadas reais. Registrar parâmetros por revisão para que um conserto não reescreva a história de aquisições.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F13-01 — Implementar rack e revisões de montagem | M4 | T-F03-05, T-F04-06 |
| T-F13-02 — Integrar descalibração por impacto | M4 | T-F13-01 |
| T-F13-03 — Criar minigames e solvers | M4 | T-F13-01, T-F05-04 |
| T-F13-04 — Integrar oficina às passadas LiDAR | M5 | T-F13-03, T-F15-02 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
