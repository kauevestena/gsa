# Parâmetros iniciais de balanceamento

Baseline P1, 13/11/2026. Valores abaixo são propostas técnicas iniciais para tornar tarefas executáveis, não resultados de testes já feitos. Mudanças exigem registrar motivo, fixture/playtest e versão. Decisões de produto confirmadas estão em [visão](visao.md).

## Tempo, jogo e controle

| Parâmetro | Valor inicial |
| --- | --- |
| Física / calendário | 60 Hz; 1 s físico = 60 s de calendário; um dia = 24 min sem pausa |
| Início | 08:00, seco, Centro Velho; 250 Coords; carrinho e câmera disponíveis |
| Processamento no escritório | 10 min de calendário por lote básico; 30 min por lote avançado; custo/tempo exibidos antes de iniciar |
| Limite de contratos | Um ativo; repetir/procedural após encerrar; mundo livre fora dos contratos |
| Prazo comum | 6 h de calendário; expira em 7,5 h; final da campanha: 24 h, expira em 30 h |
| Interação / entradas | Teclado+mouse e gamepad; remapeamento por ações; acelerar, frear, virar, interagir, coletar, mapa, buzina, pausa |
| Modos iniciais | Arcade; Cidade calma desligada, com tutorial e contratos tranquilos disponíveis; troca de ambos fora de processamento |
| Assistência | Sem penalidade por Cidade calma; dicas podem ser desligadas; legendas ligadas; tamanho de UI 100–150% |

## Plataformas, custos e progressão

| Nível | Velocidade máxima | Preço Coords | Armazenamento virtual | Autonomia física | Desbloqueio |
| --- | --- | --- | --- | --- | --- |
| 0 carrinho | 2 m/s | Gratuito | 200 unidades | 30 min | Início |
| 1 bicicleta | 6 m/s | 400 | 400 | 40 min | HIST-02 concluída |
| 2 motoneta | 12 m/s | 1200 | 800 | 50 min | HIST-03 concluída |
| 3 carro | 18 m/s | 4500 | 1600 | 60 min | HIST-04; reputação global 10 |
| 4 Kombi | 16 m/s | 14000 | 4000 | 60 min | HIST-05; reputação prefeitura 20 |
| 5 picape | 20 m/s | 30000 | 8000 | 70 min | HIST-07; reputação global 40 |
| 6 van | 18 m/s | 60000 | 16000 | 90 min | HIST-10; reputação global 60 |

Armazenamento virtual expressa capacidade de jogo: imagem=1 unidade; 1000 retornos=1 unidade. Limites reais de disco/RAM são independentes e sempre respeitados. Sensor para de capturar antes de exceder qualquer limite e informa a causa. Uma plataforma não substitui a anterior no inventário.

Contratos por nível pagam base 200, 350, 700, 1800, 4000, 7000, 12000; história paga o dobro. XP-base=100, +25 por nível, multiplicado pelo fator de qualidade; a cada 500 XP, um ponto. Seis trilhas com três postos: Pilotagem defensiva (tolerância a impacto +10%/posto), Navegação (alerta de degradação antecipado +1 s/posto), Calibração (dicas e redução de taxa 10%/posto), Processamento (tempo -10%/posto), Negociação (autorizações nos níveis 1/2/3 de hostilidade) e Turno da noite (contratos noturnos e redução de custo de iluminação 10%/posto). Nenhuma habilidade reduz numericamente um erro já medido pelo CQ.

Reputação por cliente/bairro: entrega integral +2; excelente +3; parcial +1; expiração/abandono -1; mínima zero. Global é a soma dos ganhos líquidos por cliente, não soma duplicada cliente+bairro. Desconto na loja = min(15%, reputação do cliente/1000). Seguro custa 2% do veículo por contrato, franquia de 10%, restaura veículo e sensores, nunca dados pendentes. Manutenção 1% do preço por 10 pontos de dano; combustível/energia/milho: 10 Coords por reabastecimento básico, 50 avançado; base 20/contrato; Arcade 5% do pagamento-base; armazenamento 1% por contrato; flanelinha 10; escolta 10% do pagamento-base; disfarce 200; autorização 5% do pagamento-base; multas 20 (sinal), 30 (contramão), 40 (faixa exclusiva). Tarifas são informadas antes do serviço.

Proteção contra bloqueio econômico: carrinho e kit inicial são recuperáveis gratuitamente no escritório; contratos tranquilos iniciais permanecem disponíveis e incluem milho/armazenamento/processamento básico se saldo <50. Saldo nunca fica negativo; multas acima do saldo viram dívida abatida em até 20% de receitas futuras, sem impedir o kit inicial.

## Sensores e qualidade

| Perfil | Valores iniciais |
| --- | --- |
| Câmera inicial | 1280×720 de resolução simulada; 2 Hz; FOV horizontal 90°; exposição dia 1/500 s, noite 1/60 s; readout 10 ms |
| Câmeras motoneta | Frente/trás 2 Hz; limite de arrasto 3 px; sobreposição mínima 60% nas faixas exigidas |
| 360° | Seis faces virtuais 512×512; 2 Hz; tratar costuras/oclusões por face; montagem conjunta |
| Câmeras avançadas | Picape: resolução simulada 1920×1080 a 4 Hz; van: panorâmica de seis faces 1024×1024 a 2 Hz e pavimento 1920×1080 a 4 Hz; miniaturas mantêm 256×144 |
| GNSS | 5 Hz; sigma posicional base: celular 3 m, navegação 1,5 m, DGNSS 0,5 m, RTK float 0,15 m, fix 0,03 m; multiplicar por DOP |
| Heading níveis 0–2 | 3°, 2°, 1° de sigma; correção por controle observado; não inferir heading de deslocamento parado |
| GNSS comum/célula em M1 | Erro comum com sigma e tempo de correlação explícitos; fixtures usam 0,5 m e 30 s. Viés horizontal por célula tem amplitude e fator de fachada explícitos. Não são removidos da observação sem dados de correção |
| Multiconstelação níveis 5–6 | Quatro constelações representadas por fator DOP reduzido: `1+4*(1-openness)/sqrt(4)`; perfis anteriores usam uma constelação e fórmula básica; abertura mínima para solução não muda |
| Base | Raio útil inicial 2 km; residual adicional 0,02 m/km DGNSS e 0,005 m/km RTK; abertura mínima 0,8 |
| IMU | 100 Hz; gyro noise sigma MEMS/tática/navegação: 0,005/0,001/0,0002 rad/s por amostra; accel: 0,1/0,03/0,01 m/s² por amostra |
| Bias IMU | Gyro inicial 0,002/0,0005/0,0001 rad/s; accel 0,05/0,01/0,003 m/s²; passeio por √s = 1% desses valores; fator de escala 0,001/0,0003/0,0001 |
| Filtro | q=0,1/0,01/0,001 m²/s³ para MEMS/tática/navegação; R decorre da solução e do ambiente |
| DMI | 20 Hz; sigma velocidade 0,02 m/s; fator de escala inicial 0,005; chuva permite slip de até 20% |
| LiDAR L4 | 1 canal, 10 linhas/s, 100 amostras/linha, 50 m; sigma alcance 0,02 m |
| LiDAR L5 | Dois sensores de 8 canais, 5 revoluções/s, 100 azimutes/revolução cada; 80 m; sigma 0,01 m |
| LiDAR L6 | Dois sensores de 16 canais, 5 revoluções/s, 100 azimutes/revolução cada; 120 m; sigma 0,005 m |

São perfis reduzidos para o jogo, não taxas comerciais. M1 mede orçamento; mudar perfil nominal requer repetir testes de densidade/contratos. Um perfil do nível 6 representa até 16000 raios primários/s, antes dos retornos secundários. Os alvos de contrato devem ser viáveis para esses perfis, velocidade e área de suporte; o gerador usa rotas de referência comprovadas.

Materiais (probabilidade de retorno útil a incidência normal / transmissão): asfalto 0,8/0; concreto 0,9/0; metal 0,7/0; vidro 0,1/0,7; água 0,05/0; vegetação 0,6/0,4. Multiplicar probabilidade útil por max(0,1, cos(ângulo)); chuva adiciona 2% de retornos espúrios e reduz alcance útil em 20%. Valores são didáticos e configuráveis por material.

## Contratos de referência

| Produto | Limites / excelência |
| --- | --- |
| Fachadas introdutórias | 20 IDs; completude 100%/100% binária; cobertura 90%/98%; imagens válidas 80%/98%; RMSE não exigido |
| Inventário inicial | Completude 90%/100%; RMSE 2 m/0,5 m; ≥3 checkpoints; imagens válidas 80%/98% |
| Calçada acessível | Completude 90%/100%; precisão relativa 0,20 m/0,05 m; controles locais fornecidos |
| 360° | Cobertura 95%/99%; imagens válidas 90%/99%; RMSE 0,5 m/0,1 m |
| LiDAR básico | Cobertura 90%/98%; ≥90%/98% das células com ≥5 pts/m²; RMSE 0,20 m/0,05 m; relativo 0,10 m/0,02 m |
| Engenharia | Cobertura 98%/99,5%; ≥95%/99% das células com ≥20 pts/m²; RMSE 0,05 m/0,02 m; relativo 0,03 m/0,01 m |

Completude 100% sem margem é requisito binário; não entra na média de margens. Contratos podem exigir sobreposição, privacidade e prazo como requisitos binários adicionais. Os valores de excelência são estritamente melhores que aprovação para métricas quantitativas.

## Cidade, estresse e perigos

Quatro regiões de aproximadamente 1 km² cada; a área total inclui vias e conexões. MVP ocupa um quadrante de 500×500 m, com 20 fachadas, 12 bueiros, 12 placas, 8 segmentos de calçada, parque de treino, escritório e dois pontos de descarregamento. Expansão respeita IDs existentes. Carregar células de 100×100 m num raio de 600 m do jogador, mantendo proxies distantes.

Estresse de Gimbalina ∈[0,100]: buzina +10, buraco +15, cachorro +20, fogos +25; repouso -5/s e milho -30 (cooldown 10 s). A partir de 60, oscilação cresce linearmente até 5°; em 100 por 5 s, ela foge para um ponto seguro, encerrando a saída. Recuperação no escritório, sem ferimento. Fantasias não mudam métricas.

Tráfego ativo: até 50 carros em cenário de referência, além de até 30 pedestres; distância de spawn ≥80 m e fora da câmera. Bêbados: 2% dos spawns diurnos, 8% noturnos, 12% perto de bares no fim de semana, teto 12%; zero nos contratos tranquilos. Horas de rush 07–09 e 17–19; noite 20–06. Chuva reduz aderência para 0,6 do seco. Impacto com Δv≥3 m/s descalibra rack; dano de 100 destrói veículo com efeito cartunesco.

Cidade calma suprime bêbados, cães perseguidores, sabotagem, bombas, perseguições e dano por ataques; mantém tráfego regular, regras viárias, clima e desafios de sensor. Missões perigosas usam variante de prazo/oclusão sem inimigos. O jogador pode dirigir com cuidado, limpar lente, recalibrar, negociar, contratar escolta, disfarçar e mudar horário.

## Orçamento técnico inicial

60 fps em 1920×1080; benchmark mede frame CPU/GPU, p95/p99, física, captura, filas e RAM. Referência inicial: GPU integrada de classe Radeon Vega 7, CPU 6 núcleos, 16 GiB RAM dual-channel, SSD, sem GPU dedicada; registrar modelo/driver/OS reais antes do aceite. Comparar máquinas diferentes não substitui a medição de referência.

Veículo: 1500–4000 triângulos; personagem: 1000–2500; objeto urbano: 50–500; texturas: 128–512 px. Neblina 300–600 m, iluminação por vértice + sol simples, sombras em mancha para NPCs. Memória total alvo ≤2 GiB; orçamento inicial de pontos desenhados 250 mil, reduzível por LOD sem alterar CQ.

## Primeiro incremento jogável de perigos na cidade

Complementa F16 sem alterar seus limiares/tempos físicos originais.

| Parâmetro | Valor inicial | Limite |
| --- | --- | --- |
| Atividade após foto | 0,6 s | Prévia LiDAR não vira dado científico |
| Tinta | +45% obstrução | Limpeza 3 s; sem reescrever fotos |
| Pneu furado | Máximo 6 m/s; reparo 50 C$ | Reparo parado |
| Marco deslocado | +0,8 m E / −0,3 m N | Referência publicada imutável |
| Spoofing | Até 20 s | Rampa original 10 m em 5 s |
| Perseguidor | 20 s, 14 m/s, impacto Δv 4 m/s, intervalo 3 s | Direção local com obstáculos |
| Barreira | 30 s, 22 m à frente, caixa 4 × 1,2 × 1,2 m | Sem área protegida |
| Envio de fotos | Raio 10 m de escritório/descarga/Wi-Fi | Custódia com checksum, sem autonomia extra |
| Autorização inicial | Negociação 1 | Zona/trabalho selecionados |

## Encontros cotidianos na cidade

Complemento inicial de F09, com timers físicos, independente do calendário acelerado.

| Parâmetro | Valor | Observação |
| --- | --- | --- |
| Aviso do cão | 2 s | Incluído no máximo original de 10 s |
| Velocidade do cão | 4,5 m/s | Bicicleta: até 6 m/s |
| Contato do cão | Raio 1,2 m; frenagem 2 s | Um cambaleio por encontro, sem ferimentos |
| Locais ativos | 4 cães e 4 atendentes | Próximos aos veículos estacionados, chão livre |
| Petisco | Ação gratuita | Inventário de consumíveis ainda não integrado |
| Oferta de estacionamento | Veículo parado (<0,1 m/s), raio 8 m; dono a até 12 m | Tarifa original 10 Coords, escolha explícita |
| Recusa | Aviso 3 s, depois +5 pontos cosméticos | Afastar o veículo cancela; teto visual 100 por veículo |
| Cooldown por local | 30 s após encerramento | Persistido; zero ameaças/cobranças sob proteção calma |
