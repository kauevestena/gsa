# Plano — F12

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar primeiro lotes e confirmação de descarga; depois save de estado agregado e recuperação. Escrever temporário no mesmo volume e substituir manifest somente após flush/checksum; manter geração anterior. Core replay recebe as mesmas observações serializadas pelo adaptador e usa execução ordenada. Migrações puras operam sobre cópia com testes de fixture.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F12-01 — Implementar lotes e descarregamento atômico | M2 | T-F01-02, T-F04-03 |
| T-F12-02 — Implementar save e recuperação | M2 | T-F12-01, T-F06-02, T-F08-01 |
| T-F12-03 — Implementar replay e ferramenta de relatório | M3 | T-F12-02, T-F05-02 |
| T-F12-04 — Adicionar migração e compatibilidade | M7 | T-F12-03 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
