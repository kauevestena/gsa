# Plano — F17

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Graybox valida jogo antes de produção completa. Definir preset de exportação Blender e ferramenta que verifica tags/orçamento; converter um kit piloto. Estabilizar roteiro bilingue antes de gravar, guardar aprovação/direitos por asset e substituir vozes provisórias por manifest. Cutscenes consomem eventos de campanha, sem ser proprietárias dos desbloqueios.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F17-01 — Produzir kit piloto e pipeline de importação | M1 | T-F02-01 |
| T-F17-02 — Produzir arte da cidade e frota completas | M7 | T-F17-01, T-F02-06, T-F03-08 |
| T-F17-03 — Produzir e integrar dublagem bilíngue | M8 | T-F10-06, T-F11-04 |
| T-F17-04 — Produzir rádios, trilha e mixagem | M8 | T-F17-03 |
| T-F17-05 — Integrar cutscenes e apresentação final | M8 | T-F17-02, T-F17-03, T-F10-05 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
