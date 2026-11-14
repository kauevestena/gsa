# F21 — Plataformas especiais — expansão E2

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: E2. Origem: Progressão de plataformas e sensores: Extra; Missões secundárias. Estado: especificado; não implementado.

## Requisitos

### REQ-F21-01 — Mochila LiDAR

Oferecer operação a pé em calçadão/estação de metrô, com LiDAR/IMU e controles locais sem GNSS interior.

### REQ-F21-02 — Rodoferroviário

Oferecer operação guiada por trilhos, troca rodovia/trilho em ponto autorizado e planejamento de janela de circulação.

### REQ-F21-03 — Barco

Oferecer coleta de margens com movimento/atitude da água e limitações de retorno sobre água.

### REQ-F21-04 — Missões próprias

Cada plataforma especial inclui tutorial, missão técnica e contrato procedural, ambos os modos/idiomas e persistência integrada.

## Comportamento e limites

As plataformas são compradas após a campanha, sem substituir a frota principal. Preços iniciais: mochila 20000, rodoferroviário 40000, barco 30000 Coords; três missões introdutórias permitem empréstimo gratuito do equipamento. Todas usam o pipeline existente, sem modo de pontuação paralelo.

Mochila: velocidade 1,5 m/s durante coleta, LiDAR de nível 4, IMU tática, bateria 30 min, 2000 unidades. Interior de estação exige marcos conhecidos, ZUPT e retorno aos controles; não prometer SLAM completo. Rodoferroviário: até 12 m/s em trilhos, sensor nível 5, DMI, 60 min/8000 unidades; grade de horários com bloqueio de entrada quando há trem e alternativa de janela, sem colisão inevitável. Barco: até 8 m/s, GNSS/IMU tática e LiDAR nível 5, 45 min/4000 unidades; roll/pitch oscilam pela onda simulada, e áreas de água não são exigidas como superfície levantável por LiDAR.

Missões: mochila — A Estação Sem Céu, planta de plataformas acessíveis; rodoferroviário — Trilhos no Prazo, corredor de 500 m; barco — Margens do Datum, 300 m de margem com oclusões. Cada uma tem dois contratos derivados por área/condição. Estação/rio/trilhos são expansões localizadas de setores existentes, não novas regiões obrigatórias da 1.0.

## Interfaces

Novos VehicleProfile/SensorProfile/MissionDefinition, sem quebra dos contratos de observação ou CQ. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F21-01

Dada mochila no interior sem GNSS, então controles/IMU/ZUPT permitem percurso demonstrável e CQ reprova deriva excessiva sem correção mágica.

### AC-F21-02

Dado veículo ferroviário em janela ocupada, então entrada é bloqueada com alternativa e não ocorre coleta fora do trilho por teleporte.

### AC-F21-03

Dado barco em ondas/água, então atitude altera observações e retorno de água respeita material, sem medir margem pela verdade.

### AC-F21-04

Dada cada expansão, então tutorial, missão e variantes procedurais passam em Arcade/Técnico, dois idiomas e save/reload.
