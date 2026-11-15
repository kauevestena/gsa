# Evidence — T-F04-06 (Adicionar câmera 360°)

## Escopo
Implementação do conjunto panorâmico 360° em cubemap de 6 faces (`Front`, `Right`, `Back`, `Left`, `Up`, `Down`) montado em rack conjunto no teto (`512x512` em nível 3 e `1024x1024` na Van nível 6, preservando miniaturas `256x144`), deduplicação de alvos compartilhados em costuras entre faces e filtragem de alvos ocluídos para impedir inflação artificial de cobertura, integrado ao `PlatformCameraRigBuilder` para `Car`, `Kombi`, `Pickup` e `Van`.

## Arquivos de Implementação e Testes
- [Panoramic360CameraRig.cs](../../../src/Gsa.Core/Sensors/Camera/Panoramic360CameraRig.cs)
- [PlatformCameraRigs.cs](../../../src/Gsa.Core/Sensors/Camera/PlatformCameraRigs.cs)
- [M4WorldCar360AndRtkTests.cs](../../../tests/Gsa.Core.Tests/M4WorldCar360AndRtkTests.cs) (`T_F04_06_SixFacePanoramic360RigDeduplicatesSeamsExcludesOccludedTargetsAndIntegratesWithFleetRigs`)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Core.Tests` validou resolução por plataforma, agendamento das 6 faces (+ câmera de pavimento nos veículos superiores), prevenção de contagem dupla em costuras e descarte de alvos ocluídos.

## Estado
Implementada; em revisão delegada à CI.

