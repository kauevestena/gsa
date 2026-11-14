# Plano — F11

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implantar PO e formatação em M0 antes da primeira tela. HUD recebe snapshots a 10 Hz e nunca calcula CQ. Menus de contrato/escritório chamam comandos da fachada e apresentam diagnósticos. Geopédia é catálogo de dados; revisões técnicas e linguísticas têm status próprio, sem depender da engine para leitura.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F11-01 — Criar localização e glossário inicial | M0 | T-F01-02 |
| T-F11-02 — Implementar HUD, mapa e ajustes | M2 | T-F11-01, T-F06-01 |
| T-F11-03 — Criar quadro, escritório e relatório | M2 | T-F11-02, T-F06-02 |
| T-F11-04 — Entregar Geopédia e revisão do MVP | M3 | T-F11-01, T-F10-01 |
| T-F11-05 — Completar UI técnica e Geopédia avançada | M7 | T-F11-03, T-F15-03, T-F14-04 |
| T-F11-06 — Revisar localização e acessibilidade final | M8 | T-F17-03, T-F10-05, T-F11-05, T-F18-04 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
