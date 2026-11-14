# Plano — F05

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar ruído e regime de solução em Core e testar antes do HUD. Adaptador consulta céu e ambiente; Missions gerencia contratação de base. Manter ruído comum correlacionado base/rover por tick, estado salvo e relação temporal explícita. Entregar logs desde o MVP para compatibilidade com PPK posterior.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F05-01 — Implementar observações GNSS e heading | M1 | T-F01-02 |
| T-F05-02 — Integrar céu e estado de solução | M2 | T-F02-02, T-F05-01 |
| T-F05-03 — Adicionar aluguel e DGNSS | M3 | T-F05-02, T-F08-01 |
| T-F05-04 — Adicionar RTK e instalação de base | M4 | T-F05-03 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
