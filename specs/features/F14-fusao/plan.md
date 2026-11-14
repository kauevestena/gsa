# Plano — F14

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar sensores de inércia antes do estimador. Testar F/B/Q e orientação em trajetórias analíticas. Integrar filtro forward, gating por incerteza e ZUPT, depois RTS e controles. Guardar matrizes do forward pass para o smoother e comparar contra cálculo independente pequeno, incluindo covariâncias positivas sem NaN.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F14-01 — Implementar IMU e atitude | M4 | T-F05-04, T-F13-01 |
| T-F14-02 — Implementar DMI e ZUPT | M5 | T-F14-01 |
| T-F14-03 — Integrar filtro de trajetória | M5 | T-F14-02 |
| T-F14-04 — Adicionar PPK, controles e heading duplo | M5 | T-F14-03, T-F05-04 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
