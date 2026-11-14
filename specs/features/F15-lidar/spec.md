# F15 — LiDAR, nuvens e visualização

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1, M5–M7. Origem: Núcleo de simulação MMS; Progressão de plataformas e sensores; Direção de arte e orçamento técnico. Estado: especificado; não implementado.

## Requisitos

### REQ-F15-01 — Bancada independente

Medir LiDAR experimental em M1 sem liberar equipamento no MVP; perfis jogáveis começam no nível 4.

### REQ-F15-02 — Retornos físicos didáticos

Gerar retornos por padrão de varredura, alcance, material, incidência, chuva, oclusão e múltiplos retornos conforme perfil.

### REQ-F15-03 — Nuvem estimada

Georreferenciar retornos com trajetória/montagem estimadas; armazenar originais em chunks/octree e recalcular derivados por revisão.

### REQ-F15-04 — LOD sem distorcer CQ

Visualizar nuvem ao vivo com LOD e orçamento de RAM/GPU, sem substituir amostras científicas por representantes visuais.

### REQ-F15-05 — Perfis progressivos

Entregar perfilador da Kombi, dois multicanais da picape e sensores/pavimento da van com densidade/precisão alcançáveis.

## Comportamento e limites

A bancada M1 usa pose estimada sintética conhecida para ensaio, rotulada como debug, e grava raios/tempos/retornos. Ela não depende de implementar todo o filtro inercial. A integração jogável em M5 substitui o provedor por TrajectoryEstimate real.

Varredura usa agenda em tempo físico: azimutes/canais e timestamps não mudam com fps. O adaptador pode dividir consultas por tick dentro da fila limitada; cada raio preserva a pose de aquisição. Não consultar a posição atual de objeto dinâmico para um raio antigo sem um snapshot temporal: atrasos além da janela disponibilizada geram perda explícita de amostra.

Visualização ao vivo usa posição estimada disponível; após PPK, reconstruir nuvem a partir dos retornos originais, nunca apenas deslocar o desenho. Cores podem mostrar intensidade, material de visualização, idade ou qualidade; material de verdade não vira classificação científica automática. Filtro cidade inteira em pontos é visual independente do produto coletado, desbloqueado no final.

Perfilador captura seções transversais; avanço do veículo cria cobertura longitudinal. Níveis 5/6 usam rotação multicanal e retornos de vegetação. Áreas com vidro/água podem permanecer sem retorno; contratos devem exigir superfície acessível e permitir técnicas de passadas alternativas. Nenhum peso de amostragem multiplica pontos para cumprir densidade.

## Interfaces

LidarObservation, ScanPattern, PointChunk, OctreeIndex, TrajectoryEstimate, ProductGeometry e QualityReport. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F15-01

Dado plano a distância conhecida, então retorno sem ruído corresponde à geometria e ponto georreferenciado contém o erro previsto de boresight/tempo.

### AC-F15-02

Dado mesmo percurso em velocidades diferentes, então densidade muda pela quantidade real de retornos e oclusões permanecem visíveis.

### AC-F15-03

Dado LOD/cache diferente ou chunks recarregados, então métricas de CQ permanecem iguais e memória respeita orçamento.

### AC-F15-04

Dada vegetação/vidro/água/chuva, então distribuição de retornos segue os perfis, sem duplicar superfície nem inventar pontos em lacunas.
