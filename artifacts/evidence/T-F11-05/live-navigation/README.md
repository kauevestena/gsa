# T-F11-05 — Continuação: navegação observada e desempenho

Incremento de 17/11/2026 sobre o PR #24. Conecta diagnóstico GNSS/IMU/DMI, trajetória estimada e sigma ao tablet/mapa/HUD. **O aceite visual/controle de T-F11-05 continua pendente**; testes headless não equivalem a screenshots/revisão a 150%.

## Fronteiras revisadas

- `NavigationTruthMotion` só entra no sintetizador `LiveNavigationRig`. `LiveNavigationEstimator` aceita somente observações. Inicializa posição/heading observados e velocidade pela diferença temporal de dois fixes, com incerteza conservadora derivada dos dois. Não consulta spawn, pose/velocidade verdadeiros, checkpoints ou correspondências de CQ.
- 100 Hz IMU, 20 Hz DMI e 5 Hz GNSS usam segundos físicos e as constantes dos perfis existentes. O sintetizador da cidade amostra 32 raios de céu a 5 Hz e conserva erro comum/multipath. Sem base, o HUD informa autônomo ou indisponível, sem inferir RTK pela plataforma.
- `PredictLive` conserva o modelo de seis estados, covariâncias cruzadas, simetrização/floor e atualizações existentes. Expande `F*P*F' + Q` sem alocar matrizes/história por predição após o aquecimento; `Predict` permanece disponível ao RTS. O pré-cálculo de céu conserva sua iteração sobre array; a nova API de direções retorna uma coleção somente de leitura separada.
- Estimativa continua durante interrupção, com idade GNSS e sigma/alertas reais do modelo. ZUPT exige evidência observada e DMI fresco. O traçado usa um anel de 600 posições a 10 Hz; visibilidade não para sensores.
- Carga/troca de plataforma/salto espacial limpa o diagnóstico e exige nova inicialização. Saves e dados adquiridos ficam intactos. Fotos/nuvem, processamento e pagamento da campanha ainda não usam essa trajetória transitória.

Ver [modelo, relógios e limites](../../../../docs/live-navigation.md). Sigma não é erro verdadeiro/RMSE nem certificado de precisão. A síntese usa yaw e corpo nivelado; céu por colisores locais não é uma constelação física/RF. O diagnóstico não é log persistente/replay dos sensores.

## Reprodução e evidência

`DOTNET_PROCESSOR_COUNT=1 python3 scripts/verify.py --exports host` executa testes, import, export nativo e smoke. O [relatório local](local-verification.json) registra os checks executados; [manifesto](source-manifest.json) identifica os arquivos de código sobre a base informada. Logs da [cena](city-navigation-smoke.log) e do [executável Linux](export-city-navigation-smoke-linux.log) exigem `GSA_CITY_NAVIGATION_SMOKE_OK`, além dos marcadores anteriores de tablet/save.

`LiveNavigationTests` compara predição online/história com atualizações GNSS/DMI/ZUPT e covariâncias não diagonais, verifica inicialização por observações, ausência de IMU em plataformas iniciais, heading parado, contagens/cadência, propagação em blackout, ZUPT, correções fora de época e limite/ordem do traçado. `NavigationTelemetryPresenterTests` verifica cultura, métricas ausentes e status GNSS derivado da observação.

O [registro de desempenho](prediction-performance.json) vem de stdout TRX dos próprios testes: 10.000 predições, 1.024 passos de aquecimento, alocações por thread e tempo observado localmente, além da diferença máxima de estado/covariância da sequência mista. O teste de alocação exclui construção/aquecimento e atualizações de medição; não alega que todo o ciclo de sensores/fusão seja livre de alocações. Os tempos locais não são benchmark de FPS/GPU/hardware de referência.

O smoke testa a cena real: primeiro usa o resolvedor de céu da física para inicializar a van; em seguida, um ambiente determinístico sem sinal verifica o encadeamento de outage até HUD/painel/mapa. Troca idiomas, esconde a trajetória mantendo amostragem, confere clocks/dados/bytes do save e carrega a carreira para limpar o traçado. Não usa um save do jogador.

A CI do PR deve passar no SHA final em Linux e Windows antes do merge. Exports/logs nativos ficam nos artefatos do workflow. A revisão técnica de código é delegada conforme specs/README.md; visual/playtest/hardware permanecem separados.
