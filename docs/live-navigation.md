# Navegação ao vivo na cidade

O HUD GNSS, a aba **Técnico** e o mapa da cidade agora recebem observações simuladas de navegação. Não se escolhe mais `RTK Fix` apenas porque a plataforma é um carro. Sem base de correções conectada à cena, as observações são autônomas; interrupção de sinal produz coordenada ausente.

No tablet (**R**, **Tab** ou **M**), **Mostrar trajetória estimada e eixos de incerteza no mapa** controla o desenho, sem interromper sensores. O losango identifica a estimativa; o triângulo continua sendo a posição do veículo no mapa de jogo. Os eixos mostram ±1σ marginal em E/N, na escala do mapa; não são elipse de confiança. Estimativas fora da extensão não são fixadas artificialmente na borda.

## Observações e inicialização

| Plataforma | GNSS | IMU | DMI | Heading parado |
| --- | --- | --- | --- | --- |
| A pé/carrinho/bicicleta/motoneta | 5 Hz | Ausente | Ausente | Não fornece inicialização inercial |
| Carro | 5 Hz | MEMS, 100 Hz | Ausente | Aguarda movimento para a frente |
| Kombi | 5 Hz | Tática, 100 Hz | 20 Hz | Aguarda movimento para a frente |
| Picape | 5 Hz | Tática, 100 Hz | 20 Hz | Duas antenas, baseline de 1 m |
| Van | 5 Hz | Navegação, 100 Hz | 20 Hz | Duas antenas, baseline de 1 m |

O sintetizador interpola posição, velocidade e yaw entre os endpoints da física. Para este diagnóstico, o corpo é nivelado em roll/pitch; aceleração ENU e giro de yaw geram força específica e velocidade angular, seguidas pelos erros dos perfis. Heading simples exige GNSS disponível e velocidade longitudinal para a frente ≥0,5 m/s; heading duplo pode operar parado. A inicialização exige posição e heading **observados no mesmo epoch** e uma posição GNSS anterior com até 1 s de idade. Velocidade vem da diferença temporal das duas posições observadas; sua sigma propaga as incertezas dos dois fixes, com mínimo conservador de 2 m/s, e pode ser atualizada por DMI. Não se usa o spawn, a pose verdadeira, a velocidade verdadeira ou um checkpoint para inicializar o filtro.

Os relógios de sensores são físicos: GNSS 5 Hz, IMU 100 Hz e DMI 20 Hz. Eles não usam a aceleração de 60× do calendário de contratos e não avançam quando o jogador adianta o horário. A amostragem é independente da apresentação do tablet; o rig aceita passos físicos de até 0,25 s.

## Céu e GNSS

A cena mede abertura local com 32 direções Fibonacci no hemisfério superior, alcance de 250 m, sobre os colisores ativos, a 5 Hz. Reutiliza direções e parâmetros de consulta; não presume céu aberto quando o cache do treinamento não cobre a cidade. O critério de disponibilidade e o DOP multiconstelação são os mesmos dos modelos do domínio. A altura de consulta é 1,5 m nas plataformas iniciais, 1,8 m no carro, 2,4 m na Kombi/picape e 2,8 m na van.

O modelo conserva erro comum correlacionado e viés horizontal por célula de 10 m, com amplificação quando os raios identificam metal/vidro a até 15 m. É uma aproximação local por colisores; não simula constelações/orbitas ou propagação RF. Não aplica correção de base/PPK nesta cena. O DOP usado para gerar ruído é o mesmo informado na observação.

## Estimativa e alertas

`LiveNavigationEstimator` só aceita IMU, GNSS, heading e DMI. Integra atitude complementar e o filtro de seis estados existente, com atualização de posição, velocidade e ZUPT. Correções pertencem ao epoch corrente e cada sequência é consumida uma vez. Dados atrasados/futuros não corrigem retroativamente a trajetória. ZUPT exige os critérios observados por 2 s e DMI fresco; sem essas observações, não se injeta uma velocidade zero. A parada observada também fornece estimativa de bias do giro.

Após inicializar, correções GNSS passam pelo gate de inovação (3σ por eixo e Mahalanobis 3D). Rejeição não zera a idade do último fix aceito; o painel distingue inconsistência de spoofing confirmado. Uma interrupção GNSS mantém a predição IMU e os auxílios DMI/ZUPT disponíveis. O painel informa sigma E/N/U, `sigma_3D = sqrt(trace(P_position))` e idade física da última solução. Os limiares locais do filtro são **5 s** de idade GNSS e **2 m** de sigma 3D. Texto/ícones distinguem os alertas, além da cor.

Sigma descreve a incerteza **do modelo**; não é erro verdadeiro, RMSE de checagem ou certificado de precisão. Viés/multipath não modelado pode produzir erro maior que sigma. A qualidade científica oficial continua dependendo de produto e referências independentes.

## Memória e desempenho

O histórico visual usa um anel de **600 posições a 10 Hz**, aproximadamente o último minuto após inicializar. Não guarda matrizes de transição RTS, observações ou história ilimitada. Copiar o anel para o desenho não muda o estado do estimador. O painel/mapa continuam atualizando a no máximo 10 Hz quando visíveis.

`SixStateTrajectoryKalmanFilter.PredictLive` expande a matriz de transição em blocos e reutiliza uma matriz 6×6. Após aquecer o scratch, a predição não aloca por passo. A API `Predict` original conserva os retornos de transição/história necessários ao RTS de escritório. Testes com GNSS/DMI/ZUPT e covariâncias não diagonais comparam estado/covariância com tolerância de 1e-9; a medição local de alocações está nas [evidências](../artifacts/evidence/T-F11-05/live-navigation/README.md). Isso não é uma medição de FPS/GPU ou aceite do hardware de referência.

## Persistência e limites

A trajetória é um **diagnóstico transitório**. Carregar save, trocar de plataforma, desembarcar ou um salto de posição reinicia o rig, limpa o traçado e exige nova inicialização observada. Não é replay contínuo dos sensores; RNG/história de navegação não são gravados no save v2. Fotos, contratos, dívida, prazos e observações já salvas não são alterados pelo diagnóstico.

Fotos de veículos agora guardam a pose estimada disponível, após inicialização observada, conforme a [integração de perigos](city-hazards.md). Câmeras a pé/em plataformas leves e a nuvem LiDAR ainda usam poses ideais. O filtro não aprova processamento/CQ ou pagamento. Logs persistentes, base/PPK e integração científica dos produtos continuam pendentes. Inspeção visual, controle e screenshots bilíngues a 150% continuam necessários ao aceite integral de T-F11-05.

## Verificação

`python3 scripts/verify.py --exports host` executa os testes de filtros/sensores e exige `GSA_CITY_NAVIGATION_SMOKE_OK` no editor e no export nativo. O fixture mede céu real da cena, inicializa a van com observações, injeta um ambiente sem sinal reproduzível, verifica o HUD/painel/mapa bilíngue, esconde o overlay mantendo sensores e recarrega a carreira sem reutilizar o traçado. Usa o slot temporário do smoke de saves.

Nas plataformas iniciais, a [integração de Gimbalina](city-everyday-encounters.md) perturba apenas poses verdadeiras de imagem/exposição; não fornece a oscilação verdadeira ao estimador ou à pose estimada registrada. A trajetória de navegação de veículos continua transitória.
