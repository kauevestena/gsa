# F04 — Câmeras, captura e inventário

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1–M7. Origem: Núcleo de simulação MMS; Público e objetivos pedagógicos; Progressão de plataformas e sensores. Estado: projeção/extrínsecos puros em revisão; captura e plataformas posteriores pendentes.

## Requisitos

### REQ-F04-01 — Imagem causal

Simular projeção, distorção, exposição, movimento, rolling shutter, oclusão, ruído noturno, ofuscamento e gotas com efeitos mensuráveis.

### REQ-F04-02 — Reconstrução observável

Georreferenciar por trajetória estimada e montagem; inventário usa triangulação/controle explícito, nunca profundidade verdadeira como solução.

### REQ-F04-03 — Captura compacta

Guardar tempo, pose estimada, qualidade, feições, máscaras e miniatura, mantendo IDs únicos e observações brutas para reprocessar.

### REQ-F04-04 — Configurações de câmera

Suportar câmera inicial, capacete, frente/trás, 360° e pavimento conforme plataforma, com sobreposição verificável.

## Comportamento e limites

A câmera de gameplay não é a câmera de levantamento. O adaptador renderiza alvos de sensores em texturas limitadas, amostra feições/máscaras de privacidade e mantém metadados de observação. A verdade pode sintetizar visibilidade e erro; a reconstrução recebe apenas medidas de imagem, observações de navegação e controles declarados.

Sobreposição é a fração de área útil comum entre footprints estimados de imagens consecutivas sobre a superfície exigida. Para fachadas, projetar no plano de suporte conhecido; para inventário sem plano conhecido, exigir triangulação ou classificar posição como indisponível. Valor de sobreposição sem geometria suficiente não passa como zero erro.

Qualidade separa causas: arrasto >3 px; clipping de mais de 20% da região útil; cobertura de lente >20%; objeto visível insuficiente. Ofuscamento decorre do ângulo entre câmera e sol (<10°) e aumenta clipping. Chuva gera máscaras temporais persistentes até limpeza/secagem. Ruído noturno aumenta com exposição/ganho do perfil.

Perfil de pavimento combina imagens inferiores e amostras LiDAR: produzir perfil longitudinal por faixa de 0,25 m com estaqueamento de 1 m, altura estimada e lacunas explícitas. Não inventar índices rodoviários profissionais ausentes do modelo.

## Interfaces

SensorProfile(camera), MountCalibration, ObservationBatch, CaptureBatch, FeatureObservation e ProductGeometry. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F04-01

Dado alvo planar e movimento conhecidos, quando projetar/capturar, então pixels e arrasto correspondem à fórmula dentro de 0,1 px no fixture sem ruído.

### AC-F04-02

Dadas duas imagens com baseline e depois duas degeneradas, quando triangular, então a primeira produz posição estimada e a segunda informa insuficiência sem consultar a verdade.

### AC-F04-03

Dada coleta repetida sob noite/chuva/ofuscamento, quando avaliar imagens, então métricas e miniaturas indicam o defeito e limpeza/exposição adequadas melhoram o resultado.

### AC-F04-04

Dado conjunto frente/trás, 360° ou pavimento, quando capturar, então IDs, timestamps, cobertura e limites de recursos são consistentes por sensor.
