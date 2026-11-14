# F05 — GNSS, céu e bases

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1–M4. Origem: Núcleo de simulação MMS; Público e objetivos pedagógicos; Progressão de plataformas e sensores. Estado: especificado; não implementado.

## Requisitos

### REQ-F05-01 — Observação GNSS

Gerar soluções de navegação com DOP, multipath, interrupção e precisão conforme perfil/ambiente, com RNG reproduzível.

### REQ-F05-02 — Orientação inicial

Fornecer heading observado nos níveis 0–2, explicando sua limitação e permitindo controle por feições conhecidas.

### REQ-F05-03 — Base e DGNSS

Permitir alugar ou instalar base em marco conhecido; distância/visibilidade afetam correção, dados e custo.

### REQ-F05-04 — RTK e logs

Suportar autônomo, DGNSS, RTK float/fix e logs corrigíveis para PPK, expondo estado real no HUD.

## Comportamento e limites

No MVP, GPS celular/navegação e DGNSS são os únicos posicionamentos jogáveis. O mapa de céu ajuda a planejar; nenhuma habilidade elimina erro por simples multiplicador de nota. GNSS indisponível pausa georreferenciamento dependente da solução; observações de imagem podem continuar como brutos a processar se uma trajetória puder ser estimada depois.

Instalar base exige estar a pé no marco, ocupar por 5 s físicos e ter energia/log. Alugar escolhe base disponível no mapa; painel informa custo, distância e intervalo de operação. Uma base é local e offline; não usa serviço real de correção. Remoção/interrupção corta novas correções, preservando logs anteriores. Nível 2 oferece aluguel DGNSS; instalação e RTK entram no nível 3.

Perfis do carro/Kombi são multifrequência; picape/van acrescentam multiconstelação e heading por duas antenas quando equipado. Capacidades e efeitos seguem modelos.md/parametros.md, sem exigir simulação de órbitas ou ondas de rádio.

Geração/observação e estimativa são classes distintas. Logs usam tempos/IDs para permitir reprocessamento sem ressortear erros. Falsificação de sinal das facções injeta observações deslocadas; detector pode sinalizar salto, mas não acessar o valor verdadeiro para desfazer.

## Interfaces

GnssObservation, HeadingObservation, BaseLog, WorldCell e TrajectoryEstimate; flags de solução compartilhados com UI. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F05-01

Dadas seeds e céu aberto/cânion/túnel, quando simular, então observações repetem no mesmo build e a disponibilidade/variância segue o ambiente.

### AC-F05-02

Dada base conhecida próxima e depois distante/indisponível, quando corrigir, então o erro comum é removido apenas com dados válidos e a degradação é mensurável.

### AC-F05-03

Dadas as condições de float/fix e sua perda, quando avançar a simulação, então a transição respeita os 10 s de estabilização e o HUD reflete os dados produzidos.

### AC-F05-04

Dado jogador parado sem heading confiável, quando iniciar inventário, então a posição não é completada por atitude verdadeira oculta.
