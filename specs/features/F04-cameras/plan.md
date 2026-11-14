# Plano — F04

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar primeiro projeção/fixtures sem engine, depois aquisição por render target e raios de visibilidade seguros. Armazenar observações de pixels separadas do inventário derivado. Introduzir efeitos ambientais e múltiplas câmeras conservando o scheduler por sensor. Processamento de 360° pode manter faces e indexação panorâmica sem costurar imagem full-resolution.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F04-01 — Implementar projeção e fixtures | M1 | T-F01-02, T-F02-01 |
| T-F04-02 — Integrar observações e miniaturas | M1 | T-F04-01 |
| T-F04-03 — Implementar scheduler e inventário | M2 | T-F04-02, T-F05-01 |
| T-F04-04 — Adicionar ambiente e sobreposição | M3 | T-F04-03, T-F09-02 |
| T-F04-05 — Integrar capacete e câmeras frente/trás | M3 | T-F04-04, T-F03-04 |
| T-F04-06 — Adicionar câmera 360° | M4 | T-F04-05, T-F03-05 |
| T-F04-07 — Implementar produto de pavimento | M7 | T-F04-06, T-F15-05 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
