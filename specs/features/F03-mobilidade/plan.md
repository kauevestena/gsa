# Plano — F03

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar movimento de carrinho e API comum de plataforma; anexar galinha/câmera. Depois adicionar bicicleta e motoneta sem copiar o pipeline de sensor. Criar controlador automotivo configurável e perfis de suspensão/tração para Kombi, picape e van. Alimentar impactos ao módulo de calibração e perda de dados ao de custódia por eventos idempotentes.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F03-01 — Implementar locomoção inicial e entradas | M1 | T-F01-02, T-F02-01 |
| T-F03-02 — Integrar galinha e estresse | M1 | T-F03-01, T-F04-02 |
| T-F03-03 — Adicionar bicicleta e recursos | M3 | T-F03-02, T-F08-02 |
| T-F03-04 — Adicionar motoneta e recuperação | M3 | T-F03-03, T-F12-02 |
| T-F03-05 — Implementar carro com suspensão | M4 | T-F03-04 |
| T-F03-06 — Adicionar Kombi | M5 | T-F03-05, T-F08-04 |
| T-F03-07 — Adicionar picape 4x4 | M6 | T-F03-06, T-F02-05 |
| T-F03-08 — Adicionar van MMS | M7 | T-F03-07 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
