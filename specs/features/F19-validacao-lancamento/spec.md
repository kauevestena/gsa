# F19 — Desempenho, aceite e lançamento

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1–M9. Origem: Direção de arte e orçamento técnico; Riscos e mitigação; Decisões em aberto; Arquitetura técnica. Estado: especificado; não implementado.

## Requisitos

### REQ-F19-01 — Medição reproduzível

Medir fps, frame times, física, raios, backlog, memória e montagem de conteúdo em hardware/configuração identificados.

### REQ-F19-02 — Validação de diversão

Executar playtests M2/M3 com jogadores e estudantes antes de avançar produção principal.

### REQ-F19-03 — Entrega offline

Exportar/testar Windows/Linux sem rede, com campanha, saves, idiomas, voz, controles e recuperação funcionando.

### REQ-F19-04 — Publicação preparada

Preparar título, créditos, licenças, registro institucional, classificação e pacote público gratuito/código aberto, sem presumir autorização de publicação.

## Comportamento e limites

M1 e M9 usam protocolo de validacao.md, incluindo condição extrema de chuva/noite. Se máquina referência não existir no ambiente, produzir relatório preliminar e manter gate de desempenho pendente. Não declarar 60 fps por estimativa. Otimização usa perfilador e preserva testes de dados/CQ.

M2 valida loop de um contrato antes de expandir mapa; M3 valida aprendizado/retorno aos dois públicos. Uma falha de playtest gera tarefas de correção com causa e nova sessão após alteração significativa, sem apenas ajustar o limiar de sucesso.

Release candidate precisa de zero bloqueador conhecido: campanha impossível, perda de dado protegido, duplicação de pagamento, corrupção de save sem recuperação, dependência online para jogar, falta de idioma/voz obrigatórios ou vazamento da verdade ao estimador. Defeitos menores são listados com impacto e aceite humano.

Preparação para publicação termina com pacote revisável: executáveis, código e assets com licença definida, instruções, créditos, screenshots e checklist de direitos. Upload em serviço externo não está autorizado por esta tarefa documental e requer decisão concreta do canal/conta conforme EXT-04. A versão pública só pode ser marcada distribuída após ação real confirmada.

## Interfaces

BenchmarkReport, GateReport, ReleaseManifest e manifest de direitos/dependências; nenhum serviço de telemetria obrigatório. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F19-01

Dado cenário/hardware de referência e três execuções, então relatório contém todos os campos e comprova ou reprova os limites de desempenho sem esconder perdas.

### AC-F19-02

Dadas sessões dos dois públicos, então o relatório registra números e observações do protocolo e o gate só passa com critérios atendidos.

### AC-F19-03

Dados pacotes Windows/Linux offline, então o ciclo de contrato/campanha e save funcionam com todos os recursos obrigatórios.

### AC-F19-04

Dado pacote para publicar, então cada asset/dependência tem direitos documentados e pendências institucionais não são marcadas concluídas por ausência de resposta.
