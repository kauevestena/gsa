# F02 — Mundo, semântica e geodésia

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1–M7. Origem: Mundo; Direção de arte e orçamento técnico; Público e objetivos pedagógicos. Estado: especificado; não implementado.

## Requisitos

### REQ-F02-01 — Referencial fictício

Usar ENU double, datum local e conversão de altura h/H definidos nas interfaces, preservando precisão na integração Godot.

### REQ-F02-02 — Cena semântica

Todo objeto relevante à captura deve ter ID estável, classe, material, superfícies e tags de privacidade/feição.

### REQ-F02-03 — Centro Velho

Entregar MVP de 500×500 m com alvos, escritório, Wi-Fi fictício e áreas tranquilas, preservando IDs ao expandir.

### REQ-F02-04 — Quatro regiões

Expandir a cidade a cerca de 4 km² com acessos por campanha, anel viário, pontes/túnel e desafios próprios.

### REQ-F02-05 — Céu e streaming

Pré-calcular céu por célula, versionar o mapa e carregar setores sem perder estado nem quebrar captura nas fronteiras.

## Comportamento e limites

Layout inicial em offsets ENU: Centro Velho [0,1000]×[0,1000]; Porto [0,1000]×[1000,2000]; Orla [1000,2000]×[0,1000]; Serra [1000,2000]×[1000,2000] m. MVP ocupa [0,500]×[0,500] do Centro. Cada região tem área aproximada de 1 km² incluindo conexões, sem obrigação de ocupar todo metro com edifícios.

Centro: fachadas/cânions, parque e calçadão; Porto: galpões, viaduto, trilhos; Orla: avenidas, água/vidro e lotes; Serra: encosta, terra, vegetação e túnel. O trecho de teste do túnel é acessível pela boca do Porto com a Kombi antes do desbloqueio geral da Serra, resolvendo a missão nível 4 em região normalmente de níveis 5–6.

Uma rede de oito marcos (dois por região) fornece controles ocupáveis e história geodésica; checkpoints de contratos são IDs separados. Movimento clandestino de marco altera o objeto e seu status, mantendo a referência cadastral de comparação, sem reescrever retroativamente dados coletados.

Streaming mantém faixa de segurança dos raios: célula com geometria necessária ainda não carregada suspende captura com aviso. Não tratar ausência de geometria carregada como ausência física do objeto. Proxies para céu e tráfego não substituem geometria de coleta próxima.

## Interfaces

SemanticObject, WorldCell, EvaluationReference; manifesto neutro de cena para Tools; mapa de regiões/portais. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F02-01

Dada uma posição ENU e uma altura h, quando converter para Godot e voltar e calcular H, então o round-trip obedece à tolerância e H=h-Ng.

### AC-F02-02

Dada uma rua com concreto/vidro/água e alvos, quando consultar semântica, então IDs/materiais permanecem estáveis após unload/reload.

### AC-F02-03

Dado um portal bloqueado ou uma célula ausente, quando o jogador se aproximar, então acesso respeita a campanha e a captura não produz dados falsos por streaming.

### AC-F02-04

Dado o mapa MVP e depois a cidade completa, quando carregar o mesmo save, então os alvos antigos persistem e as quatro regiões somam a área planejada.
