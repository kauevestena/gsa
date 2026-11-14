# Plano — F01

[Especificação](spec.md) · [Tarefas](tasks.md) · [Roadmap](../../roadmap.md)

## Abordagem

Implementar primeiro toolchain e projetos mínimos; depois validador de conteúdo e automação. `gsa_tools` recebe malha/proxies exportados em formato neutro; não depende de abrir a engine para validar os dados de missão. Pipeline executa ferramentas instaladas/pinadas e guarda artefatos de build por sistema. CI remota pode ser adicionada após existir um remoto autorizado; os mesmos comandos devem funcionar localmente.

## Incrementos e dependências

| Tarefa | Marco | Pré-requisitos diretos |
| --- | --- | --- |
| T-F01-01 — Fixar ferramentas e bootstrap | M0 | Nenhum |
| T-F01-02 — Criar módulos e testes mínimos | M0 | T-F01-01 |
| T-F01-03 — Implementar validação de conteúdo | M0 | T-F01-02, T-F06-01 |
| T-F01-04 — Automatizar builds e exports | M0 | T-F01-02, T-F01-03 |

O critério de aceite da tarefa valida seu incremento local; cenários relacionados são testes de integração da funcionalidade, completados conforme os marcos que entregam seus pré-requisitos. As dependências de tarefa são cumulativas com os gates do roadmap. Uma tarefa de marco posterior só inicia quando o gate anterior exigido foi aceito, mesmo que seus pré-requisitos diretos já estejam prontos. Produção de conteúdo preparatório indicada no próprio marco pode avançar antes da integração final.

## Verificação e entrega

Usar os cenários de `spec.md` e as verificações específicas de cada tarefa. Evidências incluem entradas, versões, seed e resultado quando aplicável; revisões de conteúdo registram responsável e catálogo examinado. Aplicar a [estratégia de validação](../../validacao.md). Mudança de interface, fórmula ou comportamento exige atualizar os documentos compartilhados e os requisitos afetados.
