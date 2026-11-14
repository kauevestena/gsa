# Plano — F10

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Produzir dados e roteiro junto ao incremento jogável. HIST-01 fecha M2; HIST-02/03 e cinco templates fecham M3. Ato 1 restante entra com carro; ato 2 com LiDAR/Serra; ato 3 com van e facções completas. Rotas testemunhas incluem equipamento, tempo, capacidade e métricas suficientes; geração usa esses certificados e invalida-os quando mapa/perfil muda.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F10-01 — Criar tutorial e catálogo inicial de história | M2 | T-F06-05, T-F07-04, T-F11-03, T-F12-02, T-F05-02, T-F03-02 |
| T-F10-02 — Completar MVP e gerador de cinco templates | M3 | T-F10-01, T-F05-03, T-F08-02, T-F04-05, T-F09-04, T-F07-02, T-F11-04 |
| T-F10-03 — Concluir ato 1 e escrever campanha restante | M4 | T-F10-02, T-F03-05, T-F16-02 |
| T-F10-04 — Implementar ato 2 e templates avançados | M6 | T-F10-03, T-F15-04, T-F03-07, T-F07-05, T-F16-03 |
| T-F10-05 — Implementar ato 3 e levantamento final | M7 | T-F10-04, T-F03-08, T-F02-06, T-F16-04, T-F04-07 |
| T-F10-06 — Estabilizar roteiros para gravação | M4 | T-F10-03 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
