# Plano — F15

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Bancada mede primeiro raio/retorno em plano. Depois implementar scheduler/captura, georreferenciamento e streaming de chunks; introduzir LOD sem alterar o CQ. M6 acrescenta múltiplos retornos e sensores pareados, M7 perfis de engenharia. Só substituir raycasts por profundidade GPU/GDExtension após benchmark e equivalência dos fixtures.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F15-01 — Construir bancada LiDAR | M1 | T-F02-01 |
| T-F15-02 — Integrar perfilador e georreferenciamento | M5 | T-F15-01, T-F14-03, T-F03-06 |
| T-F15-03 — Implementar octree, chunks e LOD | M5 | T-F15-02 |
| T-F15-04 — Adicionar sensores pareados e múltiplos retornos | M6 | T-F15-03, T-F03-07 |
| T-F15-05 — Entregar perfil de engenharia e otimizar aquisição | M7 | T-F15-04, T-F03-08 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
