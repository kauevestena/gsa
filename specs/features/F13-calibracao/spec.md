# F13 — Rack e calibração

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M4–M5. Origem: Progressão de plataformas e sensores; Público e objetivos pedagógicos; Núcleo de simulação MMS. Estado: especificado; não implementado.

## Requisitos

### REQ-F13-01 — Rack modular

Slots de sensores, antena, IMU, DMI, computador, armazenamento e energia validam capacidade e persistem montagem física/estimada separadas.

### REQ-F13-02 — Descalibração causal

Troca de slot, colisões e buracos perturbam montagem física e produzem assinaturas visíveis nos dados sem revelar a correção exata.

### REQ-F13-03 — Minigames de calibração

Oficina oferece calibração de intrínsecos da câmera, lever arm, boresight e offset temporal com resíduos e conjunto reservado.

### REQ-F13-04 — Assistência equivalente

Arcade executa o mesmo ajuste e cobra serviço; Técnico permite ajuste/dicas/desfazer e mede qualidade dos parâmetros resultantes.

## Comportamento e limites

Oficina fica acessível no Centro após desbloquear carro. Cada slot tem transformação nominal e compatibilidade; mover sensor invalida sua calibração estimada, mantendo histórico para reprocessar coletas antigas. Batida altera somente a montagem física a partir do tick do evento; lotes anteriores conservam montagem/revisão de aquisição.

Perturbação inicial por impacto acima do limiar: lever arm até ±0,03 m por eixo, boresight até ±0,5° e offset até ±5 ms, distribuídos com seed. UI apresenta sintomas e aviso de inspeção, não os valores verdadeiros. A calibração estima novos parâmetros a partir de alvos/passadas.

Oficina usa grade, três planos não paralelos e percurso temporal preparados. Minigames não podem ser aprovados por animação concluída: intrínsecos precisam erro de reprojeção ≤1,5 px no conjunto reservado; extrínsecos precisam resíduo ponto-plano ≤0,05 m na bancada; temporal precisa resíduo longitudinal ≤0,02 m na validação. Esses limiares são locais da oficina; contrato ainda pode exigir melhor produto.

Algoritmo de mínimos quadrados: até 50 iterações, diferenças finitas com passo relativo 1e-6 (mínimo 1e-8), amortecimento inicial 1e-3, multiplicar por 10 se custo cresce/dividir por 10 se cai; parar com melhoria relativa <1e-8 ou limite. Exibir não convergência e preservar melhor candidato válido; não substituir por calibração verdadeira.

## Interfaces

MountCalibration, RackDefinition, CalibrationDataset, CalibrationResult e receitas de processamento. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F13-01

Dado sensor movido, então nova coleta usa montagem física distinta e calibração antiga produz erro; dados antigos conservam sua revisão.

### AC-F13-02

Dado boresight/tempo perturbado, então erro cresce com alcance/velocidade como no fixture analítico e pode ser reduzido por ajuste observado.

### AC-F13-03

Dado dataset não observável ou solver sem convergência, então oficina não declara calibração concluída nem injeta verdade.

### AC-F13-04

Dado ajuste idêntico em Arcade/Técnico, então resíduo e produto são iguais, com custo/assistência conforme modo.
