# Plano — F09

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Construir tráfego regular de bancada antes de variações perigosas. Clima fornece um snapshot imutável por tick aos sensores; suas curvas visuais e físicas usam os mesmos parâmetros. Centralizar filtro CalmPolicy na criação e no processamento de eventos para impedir perigo atrasado depois da troca de modo.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F09-01 — Implementar tráfego regular de bancada | M1 | T-F02-01 |
| T-F09-02 — Implementar calendário, clima e aderência | M3 | T-F02-03 |
| T-F09-03 — Adicionar bêbados, blitz e cães | M3 | T-F09-01, T-F09-02 |
| T-F09-04 — Implementar contratos tranquilos e Cidade calma | M3 | T-F09-03, T-F11-02 |
| T-F09-05 — Integrar perigos e contramedidas urbanas | M4 | T-F09-03, T-F13-02 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
