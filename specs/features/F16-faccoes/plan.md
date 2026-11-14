# Plano — F16

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Introduzir território e medidor com carro, depois primeiras ações/avisos. Expandir contra-medidas e invariantes de custódia antes de bombas. Completar cinco facções e persistência para campanha final. Testes usam eventos dirigidos para garantir que níveis, cooldowns e calm toggle não dependam de sorte.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F16-01 — Criar territórios e identidades fictícias | M4 | T-F02-04 |
| T-F16-02 — Implementar atenção e ameaças iniciais | M4 | T-F16-01, T-F09-05 |
| T-F16-03 — Adicionar sabotagem avançada e contramedidas | M5 | T-F16-02, T-F08-04, T-F14-03 |
| T-F16-04 — Integrar facções à cidade completa | M7 | T-F16-03, T-F02-06 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
