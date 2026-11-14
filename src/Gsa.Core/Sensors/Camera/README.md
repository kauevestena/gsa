# Câmera geométrica — T-F04-01

Este incremento implementa somente pinhole, distorção radial e extrínsecos com trajetória/montagem estimadas. Captura de textura, miniaturas, visibilidade física, triangulação e efeitos ambientais pertencem aos incrementos posteriores de F04.

## Contrato e convenções

- `CameraIntrinsics` contém tamanho da imagem em pixels, `fx/fy/cx/cy` em pixels e `k1/k2` adimensionais. `Initial` representa 1280×720, FOV horizontal π/2, pixels quadrados e principal em (640,360).
- `CameraProjector.ProjectCameraPoint` usa eixos de câmera X direita, Y abaixo e Z à frente. Aplica `x=X/Z`, `y=Y/Z`, `r²=x²+y²`, `scale=1+k1*r²+k2*r⁴`, `u=fx*x*scale+cx`, `v=fy*y*scale+cy`. O domínio válido é Z>0 e pixels em `[0,width)×[0,height)`.
- `ProjectionResult` devolve apenas status e pixel nullable. Alvo atrás da câmera, fora da imagem, projeção não finita ou trajetória indisponível não produz pixel válido; profundidade/posição verdadeira não fazem parte do resultado.
- `EstimatedBodyPose` identifica explicitamente posição ENU e rotação body-to-map estimadas em segundos. `EstimatedBodyTrajectory` copia os dados e interpola posição linearmente e quaternion pelo caminho curto de SLERP. Fora do intervalo retorna indisponibilidade, sem extrapolação.
- `CameraMountEstimate` contém lever arm no corpo, rotação camera-to-body e offset de tempo estimados. `ForwardFacing` mapeia a câmera Z à frente para corpo X à frente, câmera X direita para corpo −Y, e câmera Y abaixo para corpo −Z.
- `CameraGeometry.CameraToMap` compõe os `RigidTransform` existentes. A avaliação usa `sensor_time + estimated_time_offset`, conforme `specs/modelos.md`.

`ProjectMapPoint` recebe um alvo de geometria conhecida/de simulação e a trajetória/montagem estimadas explicitamente tipadas. O resultado é uma medida de imagem, não uma posição reconstruída. Nenhum método recebe `TruthSample`; não há estimador de inventário nem acesso a profundidade real para completar uma posição.

Entradas de configuração, tempo e pose não finitas são rejeitadas. Tempos repetidos/decrescentes são inválidos na trajetória. A síntese de pixel é determinística e não adiciona ruído neste fixture.

## Verificação

Os testes `Camera*` em `tests/Gsa.Core.Tests` cobrem principal/off-axis, distorção analítica, eixos da câmera, rotação de 90°, composição lever arm/boresight, offset temporal, interpolação/inversa, caminho curto/antipodal de quaternion, limites de imagem e ausência de extrapolação. Os resíduos de boresight usam `100*sin(0.001)` e de sincronização `0.02*10 = 0.2 m`; tolerâncias geométricas seguem `max(1e-6 m,1e-8 relativo)` e pixels analíticos ficam dentro de 0,1 px.
