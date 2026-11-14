# F12 — Custódia, saves e replay

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M2–M7. Origem: Loop de jogo e estrutura; Arquitetura técnica; Missões. Estado: especificado; não implementado.

## Requisitos

### REQ-F12-01 — Custódia local

Dados pendentes pertencem ao veículo; descarga em escritório/Wi-Fi fictício protege o lote atomicamente e sem rede real.

### REQ-F12-02 — Save íntegro

Salvar progresso, relógios, RNG, ledger, inventário, montagem e dados; recuperar geração anterior em corrupção/interrupção.

### REQ-F12-03 — Replay científico e visual

Registrar observações e poses/eventos além de entradas/seeds, com versões e reprodução verificável do CQ.

### REQ-F12-04 — Evolução de formato

Versionar saves/conteúdo e migrar a versão imediatamente anterior sem destruir original; recusar versão futura com diagnóstico.

## Comportamento e limites

Custódia por lote: `buffered` → `offloading` → `protected`; destruição transforma buffered/offloading não confirmados em `lost`. Escrita de chunks e checksum precede confirmação de proteção. O manifest autoritativo decide quais lotes sobreviveram a crash: não inferir proteção de arquivo temporário. Descarregamento é idempotente pelo ID do lote.

Autosave ao aceitar, concluir descarga, liquidar contrato, comprar equipamento e terminar missão; save manual no menu quando fora de transição atômica. Salvar dados pendentes mantém seu vínculo com o veículo: recarregar não os converte em protegidos. Se ocorrer perda confirmada, estado/ledger/lotes são salvos na mesma geração antes de continuar.

Replay científico é fonte de depuração/aula; pode mostrar verdade em overlay explicitamente didático, separado do modo de coleta. Replay visual reproduz poses com interpolação e eventos de câmera/tempo; não reexecuta física esperando equivalência. Metadados incluem perfil/modelo/mapa e permitem recusar reprodução incompatível sem silenciosamente reinterpretar dados.

## Interfaces

SaveManifest, ReplayManifest, CaptureBatch estados, chunk store e transações de persistência. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F12-01

Dado crash em cada fase da descarga, então lote é recuperado como pendente ou protegido conforme commit, nunca parcialmente protegido ou duplicado.

### AC-F12-02

Dado save corrompido/futuro, então o original permanece intacto e o jogador recebe opção de geração anterior ou diagnóstico.

### AC-F12-03

Dado replay do mesmo build, então CQ/estados repetem; trocar fps de renderização não altera contagem de observações.

### AC-F12-04

Dado save da versão anterior com dívida, lote pendente e contrato ativo, então migração preserva esses estados sem criar pagamento ou proteção.
