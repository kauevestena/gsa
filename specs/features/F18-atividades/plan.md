# Plano — F18

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar coleções e persistência no MVP; cosméticos/bloopers depois dos inventários. Eventos usam rede de tráfego e CalmPolicy. Desafio/filtros entram após núcleo de sensores validado, reaproveitando medição de cabeça e visualizador sem contaminar dados de levantamento.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F18-01 — Adicionar ovos, marcos e cartas | M3 | T-F02-03, T-F11-04 |
| T-F18-02 — Adicionar álbum e cosméticos | M4 | T-F18-01, T-F08-02 |
| T-F18-03 — Adicionar eventos absurdos | M6 | T-F09-04, T-F02-05 |
| T-F18-04 — Entregar desafio, filtros e coleção completa | M8 | T-F18-02, T-F15-05, T-F10-05 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
