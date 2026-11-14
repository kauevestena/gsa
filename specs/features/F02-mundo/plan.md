# Plano — F02

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Graybox semântico precede a arte. Validar transformações e unidades em um quarteirão antes de expandir. Pré-cálculo do céu usa 32 direções distribuídas no hemisfério por célula de 10×10 m a 2 m de altura, registrando fração desobstruída; o adaptador detecta exceções de túnel/altura e obstruções móveis. Guardar hash de colisionadores estáticos. Streaming é implementado no MVP, mesmo pequeno, para testar fronteiras antes do mundo completo.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F02-01 — Criar graybox semântico e coordenadas | M1 | T-F01-02 |
| T-F02-02 — Pré-calcular visibilidade de céu | M1 | T-F02-01, T-F01-03 |
| T-F02-03 — Montar Centro Velho do MVP | M3 | T-F02-01, T-F03-01 |
| T-F02-04 — Expandir Centro, Porto e Orla | M4 | T-F02-03, T-F10-02 |
| T-F02-05 — Produzir Serra e terrenos especiais | M6 | T-F02-04 |
| T-F02-06 — Integrar cidade e rede de marcos | M7 | T-F02-05 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
