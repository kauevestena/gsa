# F07 — Escritório e processamento

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M2–M5. Origem: Núcleo de simulação MMS; Público e objetivos pedagógicos; Loop de jogo e estrutura. Estado: especificado; não implementado.

## Requisitos

### REQ-F07-01 — Processar por receita

Processar lotes descarregados em produtos versionados com receita, sem alterar observações brutas.

### REQ-F07-02 — Modos equivalentes

Arcade automatiza e cobra taxa; Técnico oferece parâmetros/minigames sobre os mesmos algoritmos e critérios.

### REQ-F07-03 — Anonimização

Borrar regiões de rostos/placas no produto e miniaturas, recusando entrega com qualquer região pendente.

### REQ-F07-04 — Ferramentas avançadas

Oferecer PPK/base, ajuste por controle, comparação por checagem, boresight e filtro de chuva/animais sem acesso a verdade para corrigir.

## Comportamento e limites

Escritório possui fila de um processamento. Antes de iniciar, apresentar dados de entrada, tempo de calendário e custo. Avançar o calendário aplica prazos; concluir processamento após expiração não permite entregar aquele contrato. Cancelar mantém brutos e descarta somente saídas temporárias; taxa só é debitada quando o serviço é iniciado e não é cobrada novamente ao retomar o mesmo job.

MVP: revisar imagens/inventário, selecionar capturas, confirmar feições, aplicar máscaras e calcular CQ. Técnico seleciona/ajusta parâmetros e valida; Arcade executa presets válidos e mostra o resultado. Não implementar minigame de boresight completo antes de M4.

Anonimização usa tags de NPC/placa para sintetizar regiões sensíveis na captura; no Técnico o jogador confirma máscaras detectadas, no Arcade o sistema as aplica. Tags ajudam a verificar conformidade, não exigem reconhecimento de pessoas reais ou serviço externo. Máscara aplicada gera nova miniatura/produto; imagens originais permanecem privadas ao save local.

Filtro avançado usa vizinhança espacial/temporal e intensidade, não rótulos de verdade de chuva/pombo/galinha: remover pontos com menos de 3 vizinhos em raio de 0,5 m e persistência em apenas uma passada, com preview e desfazer. Material/semântica do simulador serve ao teste, não ao filtro. Remoção de pontos recalcula densidade/cobertura.

## Interfaces

ProcessingRecipe, ProcessingJob, ProcessedProduct, BaseLog, QualityReport, ledger de serviços. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F07-01

Dado lote protegido, quando processar/cancelar/reprocessar, então brutos mantêm hash e produtos indicam sua receita/revisão.

### AC-F07-02

Dados parâmetros iguais em Arcade/Técnico, quando processar, então os produtos científicos e CQ são iguais; taxa/UX diferem conforme modo.

### AC-F07-03

Dada placa ou rosto em miniatura, quando entregar sem máscara, então CQ reprova; após aplicar máscara, a região não é visível no produto.

### AC-F07-04

Dados logs/controles válidos e pontos de checagem separados, quando pós-processar, então melhoria é medida sem acessar oracle no algoritmo; filtro excessivo pode reduzir cobertura.
