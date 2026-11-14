# Plano — F20

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Fixar bibliotecas/ferramentas e fixtures pequenos antes de exportar nuvens grandes. Escrever streaming em chunks, controlar cancelamento e disco cheio. Validar round-trip com leitor independente e depois abrir em CloudCompare/QGIS, registrando versões. Preservar produto/revisão e hash no sidecar.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F20-01 — Definir adaptadores e dependências de exportação | E1 | T-F19-05 |
| T-F20-02 — Implementar LAS e LAZ | E1 | T-F20-01 |
| T-F20-03 — Implementar CSV e GeoJSON sintético | E1 | T-F20-01 |
| T-F20-04 — Validar exportação em ferramentas externas | E1 | T-F20-02, T-F20-03 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
