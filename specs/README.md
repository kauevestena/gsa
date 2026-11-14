# GSA — Especificação de implementação v1.0

Baseline documental: 13/11/2026. Estado: fundação M0 implementada para revisão, após o [bootstrap T-F01-01](../artifacts/evidence/T-F01-01/README.md). Ver [evidências da continuação](../artifacts/evidence/T-F01-02/README.md); o gate ainda não foi aceito.

Continuação autorizada pelo usuário em 14/11/2026: o desenvolvimento dos incrementos M0 prossegue enquanto a verificação nativa Windows e o aceite formal permanecem pendentes. Essa autorização permite avançar no código; não transforma testes não realizados em sucesso nem marca gates ou tarefas como aceitos. Evidências e revisão humana continuam obrigatórias.

Autorização ampliada em 14/11/2026: o usuário delegou a abertura e o merge de PRs, desde que a CI passe, e pediu desenvolvimento contínuo. Branches, commits, PRs, correções de CI e merges no repositório existente estão autorizados sem nova confirmação rotineira. A revisão técnica pode ser realizada pelo agente; antes de cada merge, conferir a CI do SHA atual e eventuais bloqueios/revisões. Requisitos de inspeção visual, hardware de referência, playtest e produção humana continuam registrados conforme as evidências realmente disponíveis. Essa delegação substitui a restrição inicial a alterações apenas locais; não atribui uma licença ao jogo.

Este pacote transforma a [especificação inicial](../spec_inicial.md) em requisitos, interfaces, tarefas e critérios de aceite para desenvolvimento guiado por especificação. O documento inicial permanece como histórico. As decisões confirmadas pelo usuário nesta conversa prevalecem sobre suas alternativas em aberto.

## Começar

1. Ler [visão e escopo](visao.md), [decisões](decisoes.md) e [arquitetura](arquitetura.md).
2. Consultar [roadmap](roadmap.md) e o [índice de funcionalidades](features/README.md).
3. Consultar o estado dos incrementos M0 em [tarefas de fundação](features/F01-fundacao/tasks.md). A continuação autorizada acima preserva as pendências de plataforma e revisão. Não é necessário desenvolver os sensores avançados para começar.
4. Executar o ciclo de trabalho abaixo, usando [interfaces](interfaces.md), [modelos](modelos.md) e [parâmetros](parametros.md) como contratos compartilhados.
5. Verificar este pacote com `python3 specs/validate.py`, executado da raiz do projeto.

## Fontes de verdade

| Assunto | Documento normativo |
| --- | --- |
| Escopo, decisões e exceções ao original | [Visão](visao.md), [ADRs e pendências](decisoes.md) |
| Requisitos e origem | [Catálogo](requisitos.md), [rastreabilidade](rastreabilidade.md) |
| Comportamento de uma funcionalidade | Seu `spec.md` |
| Contratos entre módulos | [Interfaces](interfaces.md) |
| Fórmulas e limites da simulação didática | [Modelos](modelos.md) |
| Valores iniciais ajustáveis | [Parâmetros](parametros.md) |
| Ordem, tarefas e aceite | [Roadmap](roadmap.md), `plan.md`, `tasks.md`, [validação](validacao.md) |

Não resolver divergências silenciosamente no código: registrar a mudança na decisão afetada, atualizar requisito/interface, tarefas e cenário de validação antes da implementação divergente. Balanceamento pode mudar sem alterar o algoritmo, desde que a tabela e os fixtures sejam versionados juntos.

## Fluxo para agentes e revisão humana

Uma tarefa está pronta quando todas as suas dependências estão aceitas, os contratos citados estão disponíveis e nenhum bloqueio externo necessário àquela tarefa está aberto. Escolher a primeira tarefa pronta na ordem do roadmap; dentro do marco, pelo identificador. Trabalhos independentes podem ser executados separadamente, mantendo um responsável por contrato compartilhado.

Estados: `pendente → em execução → em revisão → aceita`; `bloqueada` deve informar causa, responsável e condição de desbloqueio. As caixas das tarefas só são marcadas quando aceitas. Não confundir documentação pronta com funcionalidade implementada.

O **Critério de aceite** de cada tarefa é sua condição local de conclusão. Os **Cenários relacionados** ligam o incremento aos testes da funcionalidade: quando exigirem componentes de marcos posteriores, validar o incremento com fixtures e deixar a integração para o marco correspondente. Uma tarefa de schema em M0 não exige o jogo completo de M2; a funcionalidade só fica integralmente aceita quando todos os seus incrementos e cenários passarem.

Para cada tarefa:

1. Ler `spec.md`, `plan.md`, interfaces e requisitos vinculados.
2. Registrar início e implementar apenas o incremento delimitado; criar testes de comportamento quando aplicáveis.
3. Executar os testes indicados e verificações do subsistema afetado. Não alegar aprovação de ambiente indisponível.
4. Entregar resumo da alteração, evidências reproduzíveis, limitações e atualização da rastreabilidade quando necessária.
5. Submeter a entrega concreta à revisão humana. A revisão aprova o resultado, sem exigir nova autorização para cada operação rotineira.

Evidências futuras ficam em `artifacts/evidence/<task-id>/`: comando/configuração, revisão do código, versões, seed, entradas, resultado e anexos pertinentes. Produção artística usa manifesto e revisão visual; narrativa e áudio usam checklist de conteúdo e idiomas; não criar testes artificiais para esses trabalhos.

## Pedido reutilizável de execução

> Execute a tarefa `<ID>` conforme `specs/README.md` e sua funcionalidade. Verifique as dependências, implemente o incremento, valide os critérios de aceite e registre evidências. Se encontrar conflito de especificação, apresente o conflito e a correção necessária antes de mudar o comportamento. Ao terminar, informe o que está comprovado e o que aguarda revisão.

## Convenções

- `REQ-Fxx-nn`: requisito; `AC-Fxx-nn`: cenário de aceite; `T-Fxx-nn`: tarefa; `ADR-nnn`: decisão.
- `M0` a `M9`: marcos até a 1.0; `E1` e `E2`: expansões posteriores.
- DEVE indica requisito obrigatório. Valores de balanceamento são obrigatórios como baseline inicial, mas revisáveis com evidência.
- A documentação usa PT-BR. Identificadores de código/dados usam inglês; textos apresentados ao jogador usam chaves de localização.
- O validador documental verifica estrutura, referências, cobertura e dependências. Ele não comprova matemática, diversão, performance ou execução do jogo.
