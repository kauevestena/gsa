# Modelos didáticos e critérios numéricos

As fórmulas deste documento definem a implementação inicial, não especificações de fabricantes. Parâmetros de jogo estão em [parâmetros](parametros.md). Testes analíticos desativam ruído; testes estatísticos usam seeds fixas e intervalos, não um resultado sorteado favorável. Usar double no Core.

## Georreferenciamento e tempo

`p_map = r_body(t) + R_body_to_map(t) * (lever_arm_body + R_sensor_to_body * p_sensor)`; `t = sensor_time + estimated_time_offset`. A verdade usa a montagem física e o offset físico na geração; o processamento usa os parâmetros estimados. Posição interpola linearmente; atitude usa SLERP com caminho curto. Fora do intervalo da trajetória, a observação fica inválida; não extrapolar silenciosamente.

Checks analíticos: boresight de 0,001 rad a 100 m produz aproximadamente 0,1 m de desvio lateral (comparar também com `100*sin(0.001)`); offset de 0,02 s a 10 m/s produz 0,2 m; erro de lever arm de 0,10 m sem rotação produz 0,10 m. Tolerância dos fixtures geométricos: 1e-6 m ou 1e-8 relativo, o maior. Testar rotações não nulas e inversas, além de identidade.

## GNSS e base

Simular observação posicional `z = true_position + common_error + multipath + independent_noise`. O erro comum é compartilhado com a base por tick e varia suavemente; multipath local não é removido por uma base distante. Cada componente usa RNG próprio, estado persistido e distribuição normal via Box–Muller, com algoritmo/seed fixados nos fixtures.

O grid de céu fornece abertura entre 0 e 1 e um fator DOP didático: `dop = 1 + 4*(1-openness)`. Células com abertura abaixo de 0,1 não geram posição GNSS. Multipath é um viés horizontal de direção persistente por célula, ampliado junto a fachadas metálicas/vidro. O mapa exportado para pré-cálculo inclui colisionadores estáticos e versão; objetos móveis adicionam obstrução momentânea pelo adaptador.

Perfis multiconstelação dos níveis 5–6 reduzem o termo de obstrução do DOP conforme [parâmetros](parametros.md); o efeito muda a covariância e o ruído produzido, sem simular órbitas reais. Multifrequência é capacidade necessária ao perfil RTK/PPK do jogo, sem síntese de sinal de rádio. Essas simplificações são apresentadas na Geopédia.

Base instalada em marco conhecido mede seu próprio erro. DGNSS subtrai a correção observada interpolada no tempo, acrescida de decorrelação proporcional à distância. RTK é um modelo de solução corrigida, sem simulação de portadora RF: disponibilidade/qualidade decidem o regime de ruído realmente aplicado às observações. Não mudar apenas o ícone ou o CQ.

Estado inicial: autônomo; DGNSS requer base ativa; RTK float requer perfil compatível, abertura ≥0,4 e correções; fix requer abertura ≥0,7 por 10 s e base a ≤2 km. Abaixo de 0,4 retorna ao autônomo; interrupção é `unavailable`. PPK é atributo do produto processado, nunca um sinal em tempo real.

Para PPK, registrar observações corrigíveis e log temporal da base: amostras locais com erro comum ainda presente e componente de precisão compatível com o perfil, incluindo disponibilidade. O processador remove o erro comum usando esse log e suaviza a trajetória; não consulta posição verdadeira nem gera nova aleatoriedade. Base ausente/incompatível/fora do intervalo produz diagnóstico; não concede melhoria fictícia. Simplificação é explicada na Geopédia.

## Câmeras e inventário

Pinhole: `u=fx*x/z+cx`, `v=fy*y/z+cy`, para `z>0`. Aplicar distorção radial normalizada `x_d=x*(1+k1*r²+k2*r⁴)` e equivalente em y antes de projetar. Rolling shutter usa tempo por linha proporcional ao `readout_time_s`; não simular todas as linhas no MVP, mas registrar e avaliar deslocamento nas feições amostradas.

Arrasto: `blur_px = image_plane_speed_m_s * exposure_s / gsd_m_px`, com contribuição angular calculada pela projeção de dois instantes. Movimento de Gimbalina afeta atitude da câmera por um oscilador limitado pelo estresse. Iluminação, clipping, chuva na lente, oclusão e ruído determinam máscaras/métricas e a miniatura. Miniatura: 256×144; preservar pose estimada, intrínsecos, tempos, feições visíveis, métricas e máscaras de privacidade, sem guardar imagem cheia.

Um objeto conta como visível se estiver no frustum, não ocluído em pelo menos 3 dos 5 raios de amostragem de sua superfície e ocupar ≥16 px na menor dimensão projetada. Inventário requer confirmação da classe/feição pelo jogador no Técnico; Arcade propõe a classe e permite revisar. Posições de inventário usam triangulação de dois raios observados em passadas com ângulo ≥5° ou interseção com plano conhecido explicitamente fornecido pelo contrato. Não obter profundidade verdadeira do objeto para resolver a posição estimada. Metadados de visibilidade podem usar verdade para simular a detecção, mas não fornecem coordenadas à reconstrução.

Nos níveis 0–2, orientação vem de heading de navegação/odometria de jogo observada com ruído e das feições de controle conhecidas. Isso é uma observação didática explícita, não uma IMU tática escondida. Um frame isolado pode valer como foto, mas não como ponto 3D triangulado.

## IMU, DMI e estimador

IMU: `measurement = (1+scale_error)*ideal + bias + white_noise`; `bias_next = bias + sigma_walk*sqrt(dt)*normal`. Acelerômetro observa força específica, giroscópio velocidade angular. Verdade serve somente à síntese. Gravidade local constante 9,80665 m/s². Estimar orientação integrando giroscópio com quaternion; corrigir roll/pitch por gravidade somente em estado estacionário e yaw por heading GNSS válido/duas antenas. Fusão complementar usa ganho `1-exp(-dt/2s)` nessas correções.

Estimador de translação didático Kalman de seis estados `[position, velocity]`: `F=[[I,dt*I],[0,I]]`, `B=[[0.5*dt²*I],[dt*I]]`. Entrada é aceleração medida rotacionada pela atitude estimada, removendo gravidade. `Q=q*[[dt³/3*I,dt²/2*I],[dt²/2*I,dt*I]]`; q é a densidade espectral de aceleração do perfil. GNSS observa posição com matriz `H=[I,0]` e covariância da solução. Inicializar posição na primeira solução válida, velocidade por diferença temporal e incerteza conservadora; sem solução/controle inicial, captura georreferenciada fica indisponível.

DMI fornece velocidade longitudinal com escala e derrapagem. Transformar para observação de velocidade na direção estimada, com variância lateral/vertical alta; pista molhada multiplica variância longitudinal por 25. ZUPT exige por 2 s: DMI/velocidade observada <0,05 m/s, giroscópio <0,01 rad/s e aceleração próxima da gravidade (diferença <0,1 m/s²). Atualiza velocidade zero com sigma 0,01 m/s e permite estimar bias estacionário. Sem evidência suficiente, não executar ZUPT.

Pós-processamento aplica as correções disponíveis de base, refaz o filtro e usa suavizador Rauch–Tung–Striebel de seis estados, com matrizes guardadas no forward pass. Pontos de controle entram como observações posicionais com incerteza declarada. Outliers acima de 3 sigma são sinalizados, não removidos por acesso ao erro verdadeiro. Pontos de checagem nunca entram no filtro. O perfil de navegação deve produzir menor deriva estatística que o MEMS na mesma interrupção; não garantir que todo sorteio isolado melhore.

## Calibração

Montagem física e estimada são estados distintos. Troca de slot muda a física; batidas acima do limiar perturbam a montagem física com seed, sem revelar o valor correto ao jogador. Minigames alteram os valores estimados e mostram resíduos observáveis.

- Intrínsecos: grade de coordenadas conhecidas, ao menos três poses distintas e uma quarta reservada à validação; ajustar `fx,fy,cx,cy,k1,k2` por mínimos quadrados com amortecimento e jacobiano por diferenças finitas. Poses da grade vêm do procedimento de calibração, não do oracle do mundo.
- Extrínsecos: minimizar distância ponto-plano entre duas passadas de superfícies comuns, exigindo ao menos três planos não paralelos para lever arm/boresight completos. Para oficina, ambiente e passadas são preparados para observabilidade.
- Sincronização: trecho com mudança de velocidade/atitude e referência temporal conhecida; buscar offset em [-0,1;0,1] s em passos de 1 ms e refinar localmente a 0,1 ms. Velocidade constante sozinha não separa offset de lever arm longitudinal.
- Arcade executa a mesma otimização com os mesmos dados e cobra serviço. Técnico oferece ajustes/manipulação com resíduos, dica e desfazer; sua pontuação depende dos parâmetros resultantes, não da conclusão visual do minigame.

## LiDAR e octree

Gerar raios por canal, azimute e instante segundo o perfil. Cada retorno armazena distância observada, direção do sensor, tempo, intensidade e número do retorno; normal/material são entradas do simulador, não atalhos do georreferenciamento. Ruído de distância normal, falha de retorno por material/incidência, alcance e chuva alteram os retornos efetivamente produzidos.

No perfilador, um canal e varredura transversal; no multicanal, canais verticais e azimute rotativo. Para vegetação, continuar após a primeira interseção transmissiva com avanço de 0,01 m, até três retornos, limite de alcance e energia mínima de 0,1; cada material reduz energia. Não duplicar a mesma superfície por erro de avanço. Vidro/água têm baixa probabilidade de retorno útil; valores em [parâmetros](parametros.md).

Octree usa coordenadas double, folhas de até 4096 amostras e profundidade máxima 16; folhas densas no limite são paginadas. Chunks imutáveis de até 65536 amostras, índice e checksum. RAM de nuvem limitada a 256 MiB, cache LRU e escrita incremental; árvore de LOD contém representantes para renderização, sem substituir originais no CQ. Reprocessar invalida índices derivados conforme revisão do produto.

## Controle de qualidade

O contrato define uma superfície de suporte: plano horizontal para pavimento/área, planos verticais para fachadas. Rasterizar células de 2×2 m no MVP e 1×1 m nos produtos LiDAR. Recortar cada célula pela área/superfície útil; usar essa área real no denominador. Células fora do produto não entram no cálculo. Correspondências de referência são acessadas somente pelo avaliador.

| Métrica | Definição |
| --- | --- |
| Cobertura espacial | Área útil de células com observação válida / área útil total exigida |
| Completude de feições | Quantidade de IDs exigidos documentados validamente / quantidade exigida |
| Densidade | Retornos válidos distintos / m² de suporte por célula; contrato exige fração mínima de células acima da densidade-alvo |
| RMSE absoluto | `sqrt(sum(||estimated_i-reference_i||²)/n))` em correspondências de checagem, com n≥3 e distribuição por área; unidade m |
| Precisão relativa | RMSE das distâncias ponto-plano entre passadas em superfícies correspondentes; sem passadas suficientes, indisponível/reprovado quando exigido |
| Qualidade de imagem | Fração das imagens submetidas que atendem arrasto/exposição/oclusão do perfil do contrato |
| Privacidade | Zero regiões identificadas como rosto/placa sem máscara de anonimização aplicada ao produto e às miniaturas entregues |
| Conformidade | Produto/formato corretos, prazo final não expirado e dados íntegros |

Cada amostra possui ID único: mesclar lotes/repetir upload não aumenta cobertura ou densidade por duplicação. Recoletar de fato gera amostras novas, que só contam se válidas. Filtrar/remover imagens ruins pode melhorar sua fração, mas a cobertura é recalculada sobre o conjunto restante. Sem dados ou sem correspondências exigidas, o resultado é insuficiente, nunca erro zero.

Todos os limites obrigatórios precisam passar. Para métrica mínima, margem `s=clamp((value-limit)/(excellent-limit),0,1)`; para máxima, `s=clamp((limit-value)/(limit-excellent),0,1)`. Excelente deve diferir estritamente do limite. A média das margens das métricas quantitativas aplicáveis é `q`; se só houver requisitos binários, q=0. Aprovado: `stars=3+floor(2*q+1e-9)`. Reprovado: 2 estrelas se ao menos metade das métricas aplicáveis passou, senão 1. Estrelas são apresentação; nunca compensam violação de privacidade ou outra exigência obrigatória.

Pagamento integral aceito: `floor(base*(0.8+0.2*q)*(1-late_penalty))`. Para dinheiro, Missions converte q e a fração de multa para decimal com nove casas, arredondamento de empate para longe de zero, antes de calcular; aplicar floor uma única vez no final. Isso evita que resíduo binário transforme uma proposta exata de 360 em 359 Coords. O Core mantém double para as métricas científicas.

Parcial autorizado: avaliar entregáveis independentes, exigir conformidade global e pagar a soma dos pesos aprovados vezes seus fatores; apresentar valor antes da aceitação, encerrar instância e impedir pagamento posterior da mesma coleta. Multa: `min(0.5, 0.05*late_calendar_hours)`. Prazo final por padrão é entrega +25%; nenhum pagamento após expiração. Contratos de história não permitem entrega parcial.
