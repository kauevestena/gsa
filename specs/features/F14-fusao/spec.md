# F14 — IMU, DMI, integração e pós-processamento

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M4–M5. Origem: Núcleo de simulação MMS; Público e objetivos pedagógicos; Progressão de plataformas e sensores. Estado: especificado; não implementado.

## Requisitos

### REQ-F14-01 — IMU por grau

Gerar força específica/velocidade angular com bias, passeio aleatório e escala para MEMS/tática/navegação.

### REQ-F14-02 — DMI e ZUPT

Medir deslocamento/velocidade de roda com escala/slip e aplicar ZUPT somente com evidência estacionária.

### REQ-F14-03 — Fusão causal

Estimar atitude/posição/velocidade com modelo declarado, propagando incerteza e sem utilizar trajetória verdadeira para corrigir.

### REQ-F14-04 — PPK e controle

Refazer trajetória com logs de base, suavização e controles conhecidos, avaliando separadamente nos checkpoints.

## Comportamento e limites

MEMS chega com carro; DMI/tática e PPK com Kombi; navegação com van. IMU continua durante blackout GNSS e aumenta incerteza. A estimativa fica inválida para contrato quando não atende sua precisão, mesmo se visualmente suave; HUD alerta por covariância e idade da solução.

Duas antenas da picape observam baseline de 1 m com erros próprios e fornecem heading independente do movimento; perda de uma antena remove essa atualização, sem substituir pelo yaw verdadeiro. ZUPT e pontos de controle ajudam a atravessar túnel, mas não eliminam instantaneamente erro de posição acumulado.

No Técnico, jogador escolhe base/intervalo, confirma controles e inspeciona resíduos de checagem. Controles podem ter erro informado e outliers; marcar checkpoint como controle é proibido no contrato. Reprocessamento exige observações originais e logs compatíveis; sem eles, informar o que pode ser melhorado em vez de prometer PPK completo.

## Interfaces

ImuObservation, DmiObservation, HeadingObservation, TrajectoryEstimate, BaseLog, ControlObservation e ProcessingRecipe. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F14-01

Dada trajetória analítica e sensor ideal, então integração mantém pose dentro da tolerância numérica; com bias/noise, deriva segue a configuração.

### AC-F14-02

Dado túnel com e sem ZUPT/DMI, então ensaio de seeds mede redução de deriva quando há informação válida; slip molhado não é tratado como velocidade exata.

### AC-F14-03

Dados logs de base e controles separados da checagem, então PPK melhora o conjunto apropriado e recusa informação faltante sem usar verdade.

### AC-F14-04

Dadas duas antenas e veículo parado, então heading pode ser atualizado; perder antena remove a medição e preserva flag de degradação.
