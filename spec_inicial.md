# Grand Surveyor Auto (GSA) — Especificação de Design v0.1

Oct 30, 2026 · @Kaue

## Visão geral

GSA é um jogo de mundo aberto em que o jogador sobe, contrato a contrato, de um carrinho de mão com uma galinha estabilizadora até uma van de Mapeamento Móvel Terrestre (MMS) completa. Uma missão só termina quando o dado coletado passa no controle de qualidade.

**Pitch:** GTA III encontra Euro Truck Simulator, com o rigor de um relatório de controle de qualidade.

**Premissa:** recém-formado(a) em Engenharia Cartográfica, sem emprego e sem dinheiro, o(a) protagonista Elias Anacleto herda da avó um carrinho de mão e uma galinha de cabeça excepcionalmente estável. Na cidade de São Datum, mapas desatualizados sustentam grileiros, construtoras clandestinas e burocratas. Mapear bem incomoda gente poderosa.

**Pilares de design**

1. **O dado é o troféu.** Chegar ao destino não basta: cobertura, precisão e completude decidem o pagamento.
2. **Física de sensor honesta, apresentação absurda.** Erros reais de GNSS, IMU, câmera e LiDAR, embrulhados em humor de desenho animado.
3. **Cada plataforma muda o jogo.** Uma nova plataforma ou sensor traz uma mecânica nova, não apenas números maiores.
4. **Cidade viva e hostil.** Tráfego, motoristas bêbados e facções tornam a direção parte do levantamento. Porém deverão também haver missões fáceis, sem tais dificuldadess para o aprendizado do jogador.
5. **O jogador mapeia, não atira.** O protagonista não usa armas; o perigo vem de fora e se resolve dirigindo, planejando e negociando.

**Tom:** sátira leve no espírito de Simpsons Hit & Run. Acidentes e explosões são cartunescos, sem sangue, e a galinha nunca se machuca.

## Público e objetivos pedagógicos

O jogo tem dois públicos na mesma campanha: quem joga por diversão e quem estuda Geomática, Agrimensura ou Engenharia Cartográfica. A diferença fica no nível de detalhe, não no conteúdo.

- **Modo Arcade:** processamento automático, nota em estrelas, explicações curtas e engraçadas.
- **Modo Técnico:** parâmetros de sensor visíveis, relatório de CQ com números (RMSE, densidade, cobertura) e etapa de processamento manual.
- **Geopédia:** enciclopédia bilíngue desbloqueada durante o jogo, com um verbete por conceito.

O jogo é standalone e funciona offline; a veia didática está nas mecânicas, sem depender de servidor ou plataforma externa.

| Conceito | Onde aparece no jogo | Como o jogo mede |
| --- | --- | --- |
| Georreferenciamento direto | Toda captura combina GNSS, IMU, braço de alavanca e boresight | Erro posicional dos pontos contra a geometria verdadeira do mundo |
| Calibração (braço de alavanca, boresight, offset de tempo) | Oficina; batidas e buracos podem descalibrar o rack. Dever\]ao ecistir minigames para realizar a calibração das câmeras e dos parâmetros de montagem | “Fantasmas” e duplicação entre passadas; erro cresce com a distância e a velocidade |
| Integração GNSS/INS e perda de sinal | Túneis, viadutos e cânions urbanos degradam a solução | Deriva da trajetória durante a interrupção; tipo de solução (autônoma, DGNSS, RTK float/fix, PPK) |
| Base GNSS, RTK e PPK | Instalar ou alugar base; distância da base afeta a precisão | Precisão da trajetória em tempo real e pós-processada |
| Pontos de controle e de checagem | Ocupar marcos e alvos antes ou depois da coleta | Ajuste da trajetória e RMSE nos pontos de checagem |
| Exposição e movimento | Velocidade, luz, horário, chuva na lente | Arrasto em pixels, sobreposição entre quadros, imagens descartadas |
| LiDAR | Velocidade, alcance, ângulo de incidência, oclusão, vidro e água | Densidade (pts/m²), sombras de oclusão, ruído |
| Planejamento de coleta | Rota, sentido do tráfego, número de passadas, horário | Cobertura da área e das feições exigidas no contrato |
| Privacidade (LGPD) | Rostos e placas nas imagens | Entrega recusada se houver dado pessoal visível |

## Loop de jogo e estrutura

O ciclo central é aceitar um contrato, planejar, coletar, processar e entregar. A nota do CQ define o pagamento, e o dinheiro compra plataformas e sensores que abrem contratos maiores.

&#91;embedded content: ciclo de um contrato · 7 etapas, 1 decisão\]

Abaixo do mínimo, o cliente pede retrabalho e o jogador volta à coleta; um ataque antes do descarregamento apaga a coleta inteira.

**Quadro de Contratos** (análogo ao mercado de fretes do Euro Truck Simulator). Cada contrato informa:

- Cliente: prefeitura, concessionária de energia, empresa de mapas, seguradora, construtora ou universidade.
- Área (polígono no mapa) e produto: imagens, inventário de feições, nuvem de pontos ou perfil de pavimento.
- Especificação: precisão, densidade, cobertura mínima e formato de entrega.
- Prazo em horas de jogo, pagamento, multa por atraso e ícone de risco (fácil, moderado, difícil, perigoso).

**Coleta só vale depois de descarregada.** Os dados ficam no veículo até o upload no escritório ou num ponto de Wi-Fi. Perder o veículo perde tudo o que não foi descarregado.

**Economia**

- Receita: pagamento do contrato multiplicado pelo fator de qualidade. Abaixo do mínimo, o cliente paga parcialmente ou exige retrabalho.
- Custos: combustível (milho, no início), manutenção, seguro, aluguel de base GNSS, armazenamento, multas e a taxa do flanelinha.
- Moeda fictícia (nome provisório “Coords”), que evita conversão entre R$ e US$ na localização.

**Carreira** (análogo à árvore de habilidades do Euro Truck Simulator)

- XP por entrega libera pontos em seis trilhas: Pilotagem defensiva, Navegação (tolera GNSS degradado), Calibração, Processamento, Negociação (acesso a áreas de facção) e Turno da noite.
- Reputação por cliente e por bairro libera contratos maiores e descontos em equipamentos.

**Tempo e clima.** O ciclo dia/noite e o clima afetam sensores (sombras, ruído noturno, gotas na lente, ruído do LiDAR na chuva) e tráfego (hora do rush, bêbados de madrugada).

**Entre contratos,** o jogador dirige livremente, caça colecionáveis, faz atividades paralelas e treina calibração na oficina.

## Progressão de plataformas e sensores

A progressão tem sete níveis, do carrinho de mão à van MMS profissional. Primeiro chegam mais câmeras, depois GNSS/IMU melhores e, só a partir do nível 4, o LiDAR. Cada nível traz uma mecânica nova, como o CJ subindo de bicicleta a carro em San Andreas.

| Nível | Plataforma | Sensores | Posicionamento | Mecânica nova | Desbloqueio |
| --- | --- | --- | --- | --- | --- |
| 0 | Carrinho de mão + galinha | 1 câmera de ação na cabeça da galinha | GPS do celular; georreferência por feições conhecidas | Estresse da galinha: susto vira imagem tremida | Início do jogo |
| 1 | Bicicleta cargueira + galinha | Câmera da galinha + câmera de capacete | GNSS de navegação | Ciclovias, parques e becos; cachorros perseguem | História, ato 1 |
| 2 | Motoneta com bagageiro | 2 câmeras (frente e trás) | GNSS L1 + base alugada (DGNSS) | Primeiro tráfego de rua; sobreposição entre quadros passa a contar | Compra + história |
| 3 | Carro popular velho com rack de teto | Câmera 360° multilente | GNSS multifrequência com RTK; IMU MEMS | Calibração na oficina; facções passam a mirar o carro | Fim do ato 1 |
| 4 | Kombi da firma | 360° + primeiro LiDAR perfilador | RTK/PPK; IMU de grau tático; odômetro de roda (DMI) | Nuvem de pontos ao vivo; túneis viáveis com paradas ZUPT | Compra + reputação |
| 5 | Picape 4x4 | Dois LiDARs multicanal + câmeras de alta resolução | GNSS multiconstelação com duas antenas | Estradas de terra, encostas e vegetação densa (múltiplos retornos) | Ato 2 |
| 6 | Van MMS profissional | LiDARs de alto desempenho, panorâmica e câmeras de pavimento | IMU de grau navegação + DMI + PPK | Contratos de engenharia com especificação apertada | Ato 3 |
| Extra | Mochila LiDAR, veículo rodoferroviário, barco | Variam | Variam | Metrô e calçadões, ferrovia, margens de rio | Missões secundárias |

**A galinha estabilizadora** (nome provisório: Gimbalina) usa o reflexo real de estabilização da cabeça das aves. Buracos, buzinas, cachorros e fogos de artifício enchem o medidor de estresse; milho e pausas o esvaziam. Quando chega o gimbal eletrônico, ela se aposenta como mascote no painel e volta em missões especiais.

**Rack modular.** Cada veículo tem encaixes para câmeras, LiDAR, antena GNSS, IMU, DMI, computador, armazenamento e energia. Sensores vêm de missões de história ou da loja, em graus de qualidade. Mudar um sensor de lugar altera o braço de alavanca e exige recalibrar; armazenamento e bateria limitam a duração da coleta.

Marcas de veículos e sensores são fictícias, sem paródia de fabricantes reais.

## Núcleo de simulação MMS

O mundo do jogo é a verdade de campo. Cada ponto e cada imagem coletados são comparados com a geometria real da cena, então o CQ mede erros de verdade, não uma nota inventada.

&#91;embedded content: núcleo de simulação MMS · do mundo à nota\]

Os dados percorrem o pipeline com erros, mas o mundo verdadeiro chega intacto ao CQ, que mede a diferença.

**Como uma captura nasce**

1. A física do veículo gera a trajetória verdadeira (posição e atitude) em passo fixo.
2. Os modelos de sensor amostram o mundo: raios para o LiDAR, renderização em textura para as câmeras, visibilidade de céu para o GNSS.
3. Os modelos de erro perturbam as observações conforme o grau do sensor e o ambiente.
4. A solução de trajetória funde GNSS, IMU e DMI. Em tempo real ela é pior; no pós-processamento (PPK + suavização) melhora.
5. O georreferenciamento direto leva cada observação ao referencial do mapa usando a trajetória estimada, nunca a verdadeira.

```latex
\mathbf{r}^{m}_{P}(t) = \mathbf{r}^{m}_{b}(t) + \mathbf{R}^{m}_{b}(t)\left(\mathbf{a}^{b} + \mathbf{R}^{b}_{s}\,\mathbf{r}^{s}_{P}\right), \qquad t = t_s + \delta t
```

O jogo injeta erro em cada termo: posição do corpo r(t) via GNSS, atitude R(t) via IMU, braço de alavanca a e boresight R(s→b) via calibração, e o offset de tempo δt via sincronização. Assim cada erro tem uma assinatura visível: deslocamento constante, erro que cresce com a distância ou erro que cresce com a velocidade.

| Sensor | O que é simulado | Efeito no jogo |
| --- | --- | --- |
| GNSS | Visibilidade de céu por célula do mapa (pré-calculada), DOP, multicaminho junto a prédios, interrupção em túneis e viadutos, distância até a base | Ícone do tipo de solução no HUD; camada de “céu aberto” no mapa para planejar a rota |
| IMU | Viés, passeio aleatório e fator de escala por grau (MEMS, tático, navegação) | Deriva durante interrupções; paradas ZUPT reduzem a deriva |
| DMI | Fator de escala e derrapagem | Segura a trajetória em túneis; falha em chão molhado |
| Câmera | Pinhole + distorção, exposição, arrasto, rolling shutter, ruído noturno, ofuscamento, gotas | Arrasto em pixels ≈ velocidade × tempo de exposição ÷ GSD; imagens reprovadas |
| LiDAR | Padrão de varredura (canais × taxa), alcance, ruído de distância, refletância por material, ângulo de incidência, retornos espúrios na chuva | Densidade cai com a velocidade; vidro e água viram buracos; carros estacionados fazem sombra |

**O que fica armazenado.** Imagens não são guardadas em resolução cheia: o jogo grava pose, tempo, métricas de qualidade, feições visíveis e uma miniatura. A nuvem de pontos é acumulada numa octree para medir cobertura e densidade e para ser desenhada na tela.

**Escritório.** No Modo Técnico o processamento vira minigames; no Modo Arcade é automático e cobra uma taxa.

- Pós-processar a trajetória com a base escolhida.
- Ajustar a trajetória a pontos de controle e conferir nos pontos de checagem.
- Corrigir o boresight girando duas passadas até coincidirem.
- Filtrar ruído: chuva, pombos e galinhas.
- Borrar rostos e placas antes da entrega.

| Métrica de CQ | Como é calculada |
| --- | --- |
| Cobertura | Fração das células da área e das feições exigidas com captura válida |
| Densidade | Pontos por m² em cada célula, contra o mínimo do contrato |
| Precisão absoluta | RMSE nos pontos de checagem, contra a geometria verdadeira |
| Precisão relativa | Discrepância entre passadas sobrepostas |
| Qualidade de imagem | Fração de imagens sem arrasto, sub ou superexposição |
| Conformidade | Privacidade, formato e prazo |

A nota final vai de 1 a 5 estrelas, e cada contrato define o mínimo por métrica.

## Missões

Há três fontes de missão: a campanha em três atos (roteirizada, com cutscenes), os contratos procedurais do Quadro e as atividades paralelas. A dificuldade tem quatro faixas, sempre visíveis antes de aceitar: fácil, moderada, difícil e perigosa.

| Missão | Dificuldade | Plataforma mínima | Produto | Desafio principal |
| --- | --- | --- | --- | --- |
| Fachadas da Rua do Comércio | Fácil | Carrinho de mão | Fotos de 20 fachadas para cadastro de IPTU | Iluminação e sobreposição com a galinha calma |
| Bueiros Perdidos | Fácil | Carrinho de mão | Inventário de bocas de lobo | Terminar antes da tempestade |
| Calçada Acessível | Moderada | Bicicleta | Largura de calçadas e degraus | Precisão relativa e pedestres na frente da câmera |
| Placas da Avenida | Moderada | Motoneta | Inventário de sinalização vertical | Tráfego intenso e motoristas bêbados |
| Corredor de Ônibus | Moderada | Carro | Imagens 360° do corredor | Usar a faixa exclusiva só no horário liberado |
| O Túnel | Difícil | Kombi | Nuvem de pontos do túnel | Sem GNSS: controlar a deriva com ZUPT e pontos de controle nas bocas |
| Vistoria do Viaduto | Difícil | Kombi | Nuvem sob o tabuleiro | Multicaminho e interrupções curtas de sinal |
| Noite de Chuva | Difícil | Picape | Nuvem de pontos de avenida | Ruído do LiDAR e câmeras escuras |
| Grileiros no Retrovisor | Perigosa | Carro | Divisas de lotes | Mapear antes que a facção mova os marcos; perseguição |
| Obra Fantasma | Perigosa | Kombi | Nuvem de uma obra clandestina | Coletar e descarregar antes que explodam o veículo |
| O Grande Levantamento (final) | Perigosa | Van MMS | Cidade inteira para o novo Plano Diretor | Prazo de 24 h de jogo com todas as facções contra |

**Contratos procedurais.** O gerador combina cliente, área, produto, especificação, prazo, clima, horário e risco, limitado pelo que a frota do jogador consegue entregar.

**Falha e perda parcial.** Dado abaixo do mínimo gera pagamento parcial ou retrabalho, não fim de jogo. Falha total só ocorre com prazo estourado, veículo destruído antes do upload ou galinha fugida nos níveis 0 e 1.

## Perigos e antagonistas

Os perigos atacam o dado, não só o veículo: uma batida descalibra o rack, uma bomba apaga a coleta não descarregada. O jogador nunca revida com armas.

**Motoristas bêbados.** São agentes de IA com direção errática: ruído no volante, velocidade irregular, sinal vermelho e contramão. Eles se denunciam por ziguezague, farol alto e som alto, e aparecem mais à noite, perto de bares e nos fins de semana. Blitz da Lei Seca surgem como eventos de rua que os tiram de circulação; dirigir alcoolizado é sempre perigo, nunca recompensa.

**Facções.** Todas são fictícias e vivem de mapas desatualizados, sem inspiração em grupos ou comunidades reais.

- **Sindicato do Marco Torto:** grileiros que movem marcos de divisa.
- **Cartel do Cimento:** construtora clandestina que esconde obras irregulares.
- **Máfia do Mapa Velho:** despachantes que lucram com cadastro desatualizado.
- **Sociedade da Terra Plana:** falsifica sinal GNSS e cola cartazes nas lentes.
- **MapaFácil S/A:** empresa rival que corta caminho e sabota concorrentes.

**Nível de Atenção** (análogo ao nível de procurado do GTA, mas das facções). Ele sobe enquanto o jogador mapeia o território de uma facção, em cinco estrelas: olheiros, depois tinta nas lentes e pneus furados, perseguições e bloqueios e, no topo, bomba no veículo estacionado. A polícia só aplica multas de trânsito.

| Perigo | Efeito no veículo | Efeito no dado | Contramedida |
| --- | --- | --- | --- |
| Motorista bêbado | Batida e dano | Rack descalibrado; passada perdida | Atenção, buzina, rota alternativa, habilidade Pilotagem defensiva |
| Tinta ou tomate na lente | Nenhum | Câmera cega até a limpeza | Parar e limpar; protetor de lente |
| Bomba no veículo estacionado | Veículo destruído | Perde a coleta não descarregada | Upload frequente, seguro, não estacionar no território |
| Falsificação de sinal GNSS | Nenhum | Trajetória deslocada | Perceber o salto na solução; confiar na IMU |
| Buracos e lombadas | Suspensão | Picos na IMU; boresight pode mudar | Reduzir a velocidade; recalibrar |
| Cachorro (bicicleta) | Queda | Galinha estressada | Pedalar mais rápido ou oferecer petisco |
| Flanelinha | Arranhão se recusar | Nenhum | Pagar ou estacionar longe |

**Outras saídas além de fugir:** escolta paga, disfarce (adesivo de pizzaria na Kombi), coleta de madrugada e negociação de autorização com a associação do bairro, liberada pela habilidade Negociação.

**Contratos tranquilos.** Parte dos contratos fáceis e moderados acontece sem bêbados nem facções, para o jogador aprender cada sensor sem pressão: áreas fechadas (campus, parque, condomínio), horário diurno e bairros calmos. O Quadro marca esses contratos com um selo próprio, e o ajuste “Cidade calma” desliga os perigos para quem só quer mapear.

## Fun elements

O humor vem de física exagerada e de piadas de geomática, no espírito de Simpsons Hit & Run e Goat Simulator, sem atrapalhar o núcleo técnico.

- **Galinha com física de boneco:** a cabeça fica parada enquanto o corpo sacode; o cacarejo acompanha o estresse. Fantasias: capacete, colete refletivo, óculos escuros.
- **Ovos colecionáveis:** a galinha bota ovos em lugares aleatórios, que valem dinheiro extra.
- **Marcos geodésicos perdidos:** espalhados pelo mapa; ocupar cada um libera um verbete da Geopédia sobre história da geodésia.
- **Cartas de projeção cartográfica:** álbum para completar com Mercator, Robinson, Gall-Peters e outras.
- **Álbum de Bloopers:** as câmeras flagram NPCs fazendo careta, pombos na lente e selfies da galinha, como os memes de imagens de rua.
- **Modo Nuvem de Pontos:** filtro que desenha a cidade inteira como pontos, liberado ao terminar a campanha.
- **Modo Daguerreótipo:** filtro sépia em homenagem à fotogrametria do século XIX.
- **Protagonista em ragdoll:** tropeça no carrinho de mão e voa da bicicleta, sem ferimentos.
- **Rádios fictícias:** Geodésia FM, Rádio Multicaminho e Onda Portadora, com talk show de ouvintes terraplanistas e música original.
- **Frota personalizável:** adesivos, pinturas e buzinas, inclusive uma que cacareja.
- **Eventos absurdos:** trio elétrico bloqueando a avenida, maratona no meio da coleta, revoada de pombos sincronizados.
- **Desafio da Galinha:** atravessar o calçadão no menor tempo com a cabeça da galinha dentro da tolerância.

## Mundo

São Datum (nome provisório) é uma metrópole fictícia de inspiração brasileira, dividida em quatro regiões liberadas pela campanha, como as ilhas de GTA III. Um anel viário liga as regiões; pontes em obras e um túnel interditado seguram o acesso até a hora certa.

| Região | Paisagem | Desafio de mapeamento | Níveis |
| --- | --- | --- | --- |
| Centro Velho | Ruas estreitas, calçadões, prédios altos, comércio | Cânion urbano com GNSS ruim, pedestres, fachadas | 0–2 |
| Porto e Distrito Industrial | Galpões, trilhos, viadutos, caminhões | Multicaminho sob viadutos, tráfego pesado | 3–4 |
| Orla e Zona Sul | Avenidas largas, praia, condomínios | Velocidade alta, vidro e água no LiDAR, loteamentos de grileiros | 3–5 |
| Serra e Periferia | Morros, estradas de terra, encostas, túnel | Vegetação densa, declividade, túnel sem GNSS | 5–6 |

**Mundo como verdade geodésica.** O mapa usa projeção e datum fictícios, coerentes, com rede de marcos e ondulação geoidal própria. Coordenadas aparecem em E, N e altura, e missões podem cobrar a diferença entre altura elipsoidal e ortométrica.

**Malha semântica.** Cada objeto tem classe e material: asfalto, concreto, vidro, vegetação, água ou metal. A classe define a refletância para o LiDAR e o que conta como feição de inventário.

**Escala.** O tamanho final fica em aberto; o MVP usa apenas o Centro Velho.

## Direção de arte e orçamento técnico

O visual mira a era PS2/PC de 2001: geometria simples, texturas pequenas, iluminação por vértice e neblina de distância, rodando liso em GPU integrada. Os números abaixo são alvos propostos, a validar no protótipo no Godot.

| Item | Alvo proposto |
| --- | --- |
| Veículo | 1.500 a 4.000 triângulos |
| Personagem | 1.000 a 2.500 triângulos |
| Objeto urbano | 50 a 500 triângulos |
| Texturas | 128 a 512 px, repetíveis, paleta limitada |
| Iluminação | Cor por vértice pré-calculada + sol dinâmico simples; sombra em mancha para NPCs |
| Distância de visão | Neblina entre 300 e 600 m |
| Desempenho | 60 fps em 1080p numa GPU integrada recente |
| Nuvem de pontos | Pontos desenhados na GPU com LOD por octree; orçamento definido no protótipo |

**Conforto moderno permitido:** resolução alta, tela larga, antisserrilhado e taxa de quadros estável. Um filtro opcional imita o pontilhado das TVs da época.

**HUD no estilo GTA III:** radar circular que mostra a cobertura coletada como mapa de calor, ícone do tipo de solução GNSS, luzes de status dos sensores, armazenamento, bateria, estresse da galinha, dinheiro e Nível de Atenção.

**Pipeline de arte:** Blender para glTF 2.0, kit modular de quarteirões (lotes, calçadas, esquinas, cruzamentos) e atlas de texturas. Todo objeto sai do Blender com classe e material marcados, porque os sensores dependem disso. Cutscenes rodam no próprio motor, como em GTA III.

## Escolha de engine

**Recomendação: Godot 4.** Com a UTFPR como titular, a Unity deixa de ser gratuita: a Personal não vale para a instituição, e a licença educacional não permite distribuir o jogo. Sobraria a Unity Pro, paga por desenvolvedor a cada ano. O Godot tem licença MIT: sem custo, sem limite de receita e sem restrição para a universidade publicar o jogo ou abrir o código.

| Critério | Unity 6.3 LTS | Godot 4.7 | Unreal 5.8 |
| --- | --- | --- | --- |
| Linguagem | C#, com Burst e Jobs para cálculo pesado | GDScript (sintaxe parecida com Python) e C#; C++ ou Rust para trechos críticos | C++ e Blueprints |
| Visual nível GTA III | Adequado, com pipeline leve (URP) | Adequado e leve | Excessivo: motor pensado para alta fidelidade |
| Veículos e mundo aberto | Física de rodas nativa; pacotes prontos de veículo, tráfego e cidade low-poly na loja | Corpo de veículo nativo com Jolt; tráfego e ferramentas de cidade a construir | Veículos Chaos, tráfego em massa e coordenadas em dupla precisão nativos |
| LiDAR simulado | Raios em lote (RaycastCommand) com Jobs e Burst | Raios pela física; C++ ou Rust se faltar desempenho | Raios nativos; o CARLA serve de referência de sensores |
| Ferramentas | Editor maduro; Timeline e Cinemachine; pacote Localization | Editor maduro e leve; localização nativa | Editor muito completo, mas pesado para PCs modestos |
| Licença | Proprietária. Para a UTFPR: Personal não se aplica; licença educacional só para ensino, sem distribuir o jogo; Pro a US$ 2.310 por desenvolvedor ao ano | MIT: grátis e de código aberto | Grátis; 5% de royalties acima de US$ 1 milhão por produto |
| Horizonte | 6.3 LTS com suporte até dez/2027 | Série 4.x com poucas quebras; 4.8 em beta | 5.8 é a última grande versão do UE5; acesso antecipado ao UE6 previsto para o fim de 2027 |

Versões conferidas em 30/10/2026 e licenças em 03/11/2026; links em Fontes, no fim do documento.

**Por que a licença decide.** A Unity Personal exige receita e financiamento da entidade abaixo de US$ 200 mil nos últimos 12 meses, inclusive para uso educacional, e a UTFPR passa disso. A licença educacional gratuita (Education Grant License) serve só para ensino e aprendizagem e não cobre a distribuição do que for criado com ela. Assets comprados na loja da Unity também não poderiam ir para um repositório público.

**O que o Godot cobra em troca:** mais ferramentas de tráfego e de cidade feitas pela equipe, e menos pacotes prontos. Assets de licença aberta (CC0) e o kit modular de quarteirões cobrem boa parte do visual, e o GDScript facilita a contribuição de estudantes em missões.

**A Unity continua possível** se a UTFPR ou um financiamento pagar a Pro para cada desenvolvedor. O núcleo em C# mantém essa porta aberta sem retrabalho.

**Unreal fica fora:** pesado para o visual-alvo e para os PCs dos estudantes, com iteração mais lenta em C++ e a transição para o UE6 já anunciada. O CARLA, simulador aberto de direção autônoma sobre Unreal, com LiDAR, câmeras, GNSS e IMU, vale como referência de modelagem de sensores, não como base do jogo.

**Rust deixa de ser requisito.** Bevy e Fyrox só voltam à mesa se o objetivo for um projeto 100% Rust. No Godot, C++ ou Rust podem entrar via GDExtension num trecho crítico, se o perfilador pedir.

**Protótipo de 2 a 3 semanas no Godot:** um quarteirão, carrinho de mão com galinha, uma câmera, um LiDAR simulado, 50 carros de tráfego e um contrato. O objetivo é medir fps em GPU integrada, raios de LiDAR por segundo e o tempo para montar o quarteirão antes de investir no resto.

## Arquitetura técnica

O código se divide em um núcleo de simulação em C# puro, sem dependência de engine, e uma camada de jogo fina. O Godot usa o núcleo pela versão .NET; se a Unity entrar um dia, muda só o adaptador. O jogo é standalone: roda offline, sem servidor e sem integração com outros projetos.

&#91;embedded content: arquitetura · camada de jogo, adaptador e núcleo\]

A camada de jogo só conversa com o núcleo pelo adaptador; as ferramentas de linha de comando usam o núcleo diretamente.

- **gsa\_core:** CRS do jogo, modelos de sensor e de erro, solução de trajetória, georreferenciamento direto, octree da nuvem e métricas de CQ. Biblioteca .NET com coordenadas em dupla precisão, determinística (semente fixa) e testada fora da engine.
- **gsa\_missions:** contratos e missões descritos em arquivos de dados (JSON), gerador procedural, economia e carreira.
- **gsa\_engine (adaptador):** liga o núcleo à engine escolhida; lê poses da física, dispara raios, recebe quadros das câmeras e desenha a nuvem.
- **Projeto na engine:** mundo, veículos, IA de tráfego, HUD, áudio, cutscenes, saves e scripts de missão em GDScript.
- **gsa\_tools:** linha de comando para validar missões, pré-calcular a visibilidade de céu por célula do mapa e gerar relatórios.

**Desempenho.** A visibilidade de céu do GNSS é pré-calculada, então em tempo real só há consulta. O LiDAR tem orçamento fixo de raios por quadro, disparados em lote fora da thread principal; para sensores densos, a alternativa é ler profundidade renderizada na GPU. A nuvem fica em blocos de octree no disco, com LOD para desenhar.

**Testes.** Cada efeito de erro tem teste contra a fórmula analítica: deslocamento ≈ distância × erro angular de boresight; erro ao longo da via ≈ velocidade × δt.

**Replay.** Entradas e sementes gravadas permitem rever uma coleta inteira, útil para depuração e para discutir em aula.

**Exportação opcional (Modo Técnico):** nuvem em LAS/LAZ, trajetória em CSV e inventário em GeoJSON, para abrir em CloudCompare ou QGIS. Fica em aberto se entra na 1.0.

## Localização bilíngue (EN-US / PT-BR)

Os dois idiomas entram no pipeline desde o primeiro protótipo, não como tradução no fim. Nenhum texto fica fixo no código; tudo passa por chaves.

- **Formato:** CSV ou gettext (.po), ambos nativos no Godot. O formato precisa tratar plural e concordância de gênero do português.
- **Troca a qualquer momento** no menu; o padrão vem do idioma do sistema.
- **Números e coordenadas:** vírgula decimal em PT-BR (1.234,56) e ponto em EN-US (1,234.56). Isso pesa em um jogo cheio de coordenadas e precisões. Unidades sempre métricas.
- **Glossário técnico** mantido como planilha: boresight, lever arm / braço de alavanca, point cloud / nuvem de pontos, ground control point / ponto de controle. A Geopédia usa o mesmo glossário.
- **Humor é reescrito, não traduzido:** trocadilhos, nomes de rádio, facções e marcas têm versão própria em cada idioma.
- **Espaço na interface:** o texto em PT-BR costuma ser mais longo; HUD e menus são testados nos dois idiomas.
- **Vozes:** dublagem completa nos dois idiomas, só legendas, ou falas inventadas com legenda. A decisão fica em aberto.

## Escopo, MVP e roadmap

O MVP é uma fatia vertical do Centro Velho com os níveis 0 a 2. Ela responde se coletar dados com a galinha é divertido antes de construir a cidade inteira. As datas dependem do tamanho da equipe, ainda indefinido.

&#91;embedded content: roadmap · 6 fases e 5 portões, sem escala de tempo\]

Cada fase só começa quando o portão anterior é cumprido; o portão da fase 2 é o que mais pesa, porque valida a diversão antes do investimento grande.

**Dentro do MVP**

- Centro Velho, ciclo dia/noite, tempo seco e chuva.
- Níveis 0 a 2: carrinho de mão, bicicleta e motoneta.
- Câmeras e GNSS; ainda sem LiDAR.
- Cinco contratos procedurais e três missões de história.
- Motoristas bêbados e contratos tranquilos.
- HUD, Quadro de Contratos, escritório simplificado e CQ com nota.
- PT-BR e EN-US completos; Windows e Linux.

**Fora do MVP**

- LiDAR, IMU de grau tático e calibração na oficina.
- Facções e Nível de Atenção.
- Demais regiões, rádios e cutscenes completas.
- Exportação LAS/LAZ e GeoJSON.

## Riscos e mitigação

O maior risco é o escopo: mundo aberto com simulação técnica é grande demais para uma equipe pequena sem cortes firmes. O segundo é o título, que parodia diretamente uma marca registrada.

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Escopo de mundo aberto | Projeto não termina | MVP de uma região; kit modular de quarteirões; cortar regiões antes de cortar o núcleo |
| Título “Grand Surveyor Auto” | Contestação de marca ao publicar | Usar GSA como codinome interno; definir título público próprio antes de divulgar |
| Mudança de licença ou política da engine | Custo inesperado ou restrição de distribuição | Núcleo em C# independente da engine; versão LTS fixada; licença conferida antes de começar |
| Desempenho do LiDAR simulado | Quedas de fps | Orçamento de raios por quadro, profundidade na GPU, LOD da nuvem |
| Rigor contra diversão | Chato para leigos ou raso para estudantes | Modos Arcade e Técnico; playtests com os dois públicos |
| Conteúdo sensível (bêbados, facções, explosões) | Classificação indicativa alta; leitura estigmatizante | Tom cartunesco, jogador sem armas, facções sem referência a grupos ou comunidades reais |
| Marcas e música de terceiros | Problema de licença | Marcas fictícias sem paródia de fabricantes; trilha original ou com licença aberta |
| Dois idiomas com equipe pequena | Tradução atrasa o lançamento | Chaves de texto desde o início; glossário único; humor escrito por idioma |
| Propriedade intelectual da UTFPR | Publicação travada ou fora das regras da universidade | Combinar registro do software e licença do código com a Agência de Inovação antes de publicar |

## Decisões em aberto

As três primeiras travam o resto do planejamento: engine, tamanho da equipe e finalidade do jogo.

- [x] Engine: Godot 4, porque a UTFPR é a titular; Unity só com orçamento para a Pro.
- [ ] Equipe e prazo: quantas pessoas e em quanto tempo, para datar o roadmap.
- [ ] Finalidade e distribuição: jogo comercial, gratuito, código aberto; itch.io, Steam ou distribuição direta; registro e licença do código combinados com a Agência de Inovação da UTFPR.
- [ ] Título público, mantendo GSA como codinome.
- [ ] Nome da cidade e protagonista fixo ou personalizável.
- [ ] Confirmar que o jogador nunca usa armas.
- [ ] Classificação indicativa alvo (10, 12 ou 14 anos).
- [ ] Vozes: dublagem nos dois idiomas, só legendas ou falas inventadas.
- [ ] Escala do mundo e número de regiões na 1.0.
- [ ] Exportação LAS/LAZ e GeoJSON na 1.0 ou depois.
- [ ] Plataformas além de Windows e Linux: macOS, Steam Deck.

## Fontes

- [Unity: elegibilidade das licenças Personal, estudante e educador](https://support.unity.com/hc/en-us/articles/28114350573460)
- [Unity: a Education Grant License não cobre distribuição](https://support.unity.com/hc/en-us/articles/37441530475668--Can-I-make-a-commercial-game-with-Unity-Educational-Grant-License)
- [Unity: critérios de elegibilidade da Education Grant License](https://unity.com/legal/egl-eligibility-criteria)
- [Unity: planos e preços em 2026](https://unity.com/products/pricing-updates)
- [Unity 6: suporte das versões LTS](https://unity.com/releases/unity-6/support)
- [Agência de Inovação da UTFPR](https://www.utfpr.edu.br/inovacao/agencia)
- [Unreal Engine 5.8 e planos para o UE6 (WN Hub)](https://wnhub.io/news/engines/item-51157)
- [Unity x Unreal em 2026: licenças e royalties (Tech Insider)](https://tech-insider.org/unity-vs-unreal-engine-2026/)
- [CARLA: publicações e versões](https://carla.org/posts)
- [Godot 4.6 e 4.7: novidades de 2026](https://gtstu.com/?p=4758)
- [Godot 4.8 beta 1 (blog oficial)](https://godotengine.org/article/dev-snapshot-godot-4-8-beta-1/)
- [godot-rust: crate godot no docs.rs](https://docs.rs/crate/godot/latest)
- [Bevy: versões publicadas no docs.rs](https://docs.rs/crate/bevy/latest)
- [Bevy em 2026: guia para desenvolvedores independentes (StraySpark)](https://www.strayspark.studio/blog/bevy-rust-game-engine-2026-indie-guide)
- [Fyrox 1.0.0 (blog oficial)](https://fyrox.rs/blog/post/fyrox-game-engine-1-0-0/)
