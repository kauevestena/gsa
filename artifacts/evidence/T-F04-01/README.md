# Evidência — T-F04-01

Estado: em revisão. 34 testes puros passaram para pinhole, distorção radial, dimensões/FOV, eixos da câmera, extrínsecos e transformação explícita câmera→corpo→ENU. Ver [execução integrada M1](../T-F02-01/README.md).

Fixtures analíticos cobrem lever arm de 0,1 m, boresight de 0,001 rad a 100 m e offset de 0,02 s a 10 m/s, com resultados previstos independentemente. Trajetória estimada interpola posição e quaternion pelo caminho curto, sem extrapolação; observação atrás da câmera, fora da imagem ou sem trajetória aplicável retorna estado inválido.

A saída de projeção contém pixel/status, sem profundidade ou pose verdadeira. Não há renderização/captura, triangulação, rolling shutter completo, chuva/noite ou CQ implementados nesta tarefa. Contratos e limites estão em `src/Gsa.Core/Sensors/Camera/README.md`. Sem seed: fixtures determinísticos, tolerâncias geométricas da especificação. Logs/TRX em `../T-F02-01/linux/`.
