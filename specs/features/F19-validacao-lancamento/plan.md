# Plano — F19

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Integrar instrumentação ao protótipo; separar medição automatizada de revisão/playtest. Relatórios vinculam build/hash/seed e tarefa. Preparar exports localmente antes de resolver publicação institucional; não adiar teste de plataforma até a última etapa. M9 executa campanha e recuperação em máquinas limpas.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F19-01 — Instrumentar e medir protótipo | M1 | T-F03-02, T-F04-02, T-F15-01, T-F09-01, T-F17-01 |
| T-F19-02 — Validar primeira experiência completa | M2 | T-F10-01, T-F11-03, T-F12-02, T-F07-04, T-F06-05 |
| T-F19-03 — Validar MVP com os dois públicos | M3 | T-F19-02, T-F10-02, T-F09-04, T-F07-02, T-F12-03, T-F18-01 |
| T-F19-04 — Otimizar e medir cidade completa | M7 | T-F15-05, T-F02-06, T-F17-02 |
| T-F19-05 — Produzir e validar release candidate | M9 | T-F19-04, T-F11-06, T-F17-04, T-F17-05, T-F18-04, T-F10-05, T-F12-04 |
| T-F19-06 — Preparar distribuição e resolver gates externos | M9 | T-F19-05 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
