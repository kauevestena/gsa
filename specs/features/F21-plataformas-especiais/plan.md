# Plano — F21

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar mobilidade da mochila e montagem usando APIs de plataforma; acrescentar restrições de trilho e depois flutuação simplificada. Conteúdo adicional deve reusar semântica, streaming, controles/checagem e geração factível. Textos/vozes novos passam pelo mesmo pipeline bilíngue antes de publicar a expansão.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F21-01 — Implementar mochila e levantamento interior | E2 | T-F19-05 |
| T-F21-02 — Implementar veículo rodoferroviário | E2 | T-F21-01 |
| T-F21-03 — Implementar barco e margens | E2 | T-F21-01 |
| T-F21-04 — Entregar missões e produção da expansão | E2 | T-F21-02, T-F21-03 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
