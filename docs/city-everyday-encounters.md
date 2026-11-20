# Cães e estacionamento na cidade

Este incremento liga à cena os encontros cotidianos de F09 que já tinham modelos de domínio. Complementa a [integração de facções](city-hazards.md); não encerra o playtest completo da cidade.

## Onde e como interagir

Há quatro cães e quatro atendentes de estacionamento, com modelos simples e rótulos PT-BR/EN-US. Cães ficam próximos aos veículos estacionados do Centro Velho, Porto, Orla e Serra. Atendentes ficam perto das vagas de carro, Kombi, picape e van. O posicionamento busca chão livre até 12 m da referência, evitando obstáculos e áreas protegidas. O encontro exige proximidade e linha de visada; não aparece um novo NPC sobre o jogador.

| Encontro | Regra | Saídas |
| --- | --- | --- |
| Cão | Só inicia para bicicleta a até 30 m; aviso de 2 s antes de mover; duração total máxima de 10 s | Pedalar para distância >40 m, oferecer petisco ou desembarcar |
| Movimento do cão | Até 4,5 m/s; bicicleta alcança 6 m/s; respeita obstáculos locais | Rota livre e distância; sem perseguição ilimitada |
| Contato | Um cambaleio por encontro, com frenagem por 2 s | Retomar o movimento; Gimbalina permanece ilesa |
| Petisco | E durante perseguição, ou botão no Escritório; gratuito neste modelo | Encerra a perseguição, sem cobrança ou compra repetida |
| Estacionamento | Veículo motorizado parado a até 8 m; E abre as escolhas no tablet | Pagar 10 C$, recusar ou afastar o veículo |
| Recusa | Aviso de 3 s antes de 5 pontos cosméticos | Mover o veículo além de 8 m antes do fim do aviso |
| Cooldown | 30 s físicos após encerrar o encontro, por local | Evita reinício imediato; persiste em save |

O petisco usa a ação gratuita do modelo atual; ainda não há inventário de petiscos. Recusa explícita é necessária para a retaliação; falta de saldo mantém a escolha aberta. Desembarcar mantém a posição do veículo estacionado para a recusa; afastar só o personagem não move o veículo. Trocar a plataforma encerra uma oferta referente ao veículo anterior. A cobrança usa o ID persistido da oferta e o livro-caixa, evitando pagamento duplicado.

## Dano cosmético e observações

Arranhões são registrados por plataforma, de 0 a 100 pontos cosméticos. Não entram na condição mecânica, na descalibração do rack, no armazenamento nem no CQ. Repor um veículo perdido limpa seus arranhões. Nesta versão, o indicador textual mostra o dano; decalques e animação de queda continuam pendentes de produção visual.

O encontro com cão acrescenta os 20 pontos de estresse já definidos para Gimbalina. Sua oscilação física acima de 60 agora entra nas poses verdadeiras de início/fim de exposição e readout das câmeras do carrinho/bicicleta. A pose estimada não recebe essa oscilação desconhecida. O desfoque resulta das projeções da imagem; não há penalidade de qualidade adicionada por uma pontuação de perigo. Fotografias noturnas usam o tempo noturno do perfil, corrigindo o uso anterior do intervalo diurno no fim da exposição.

Fotos já coletadas permanecem imutáveis. Estresse, fase de oscilação, cooldown do milho e retirada segura são salvos. A retirada bloqueia nova coleta até recuperar Gimbalina perto do escritório/descarga, sem ferimentos nem perda dos dados gravados. O botão de recuperação é gratuito e preserva armazenamento/autonomia. Uploads continuam usando a custódia de dados do incremento anterior.

## Cidade calma e contratos tranquilos

O ajuste global, contrato `Calm` e áreas seguras cancelam perseguição, frenagem pendente, oferta/retaliação e novas cobranças, inclusive quando ativados durante um aviso. NPCs permanecem como figuras neutras. Sair da proteção não reativa o ataque cancelado; cada local mantém seu cooldown. Calma não reescreve fotos nem remove arranhões ou estresse anteriormente acumulado.

## Saves

O envelope permanece v2; o catálogo passa de 3 para 4 para evitar que um build anterior descarte os novos estados. Saves de catálogos 1–3 são convertidos em memória e conservam fotos, custódia, danos de facção e livro-caixa. Ausência de histórico de encontros/estresse usa valores iniciais; carregar não reescreve o arquivo original.

São persistidos IDs/sequência, relógio físico, cooldowns por local, cão/posição/contato, tempo de cambaleio, oferta e contagem da recusa, arranhões por veículo e estado de Gimbalina. Versões futuras/inválidas bloqueiam a sobrescrita. Não há replay completo de tráfego ou navegação.

## Verificação e limites

`python3 scripts/verify.py --exports host` exige `GSA_CITY_EVERYDAY_SMOKE_OK` no editor e export nativo. O fixture aciona os callbacks reais, verifica aviso/petisco, frenagem do simulador de bicicleta, desfoque noturno/pose estimada, save durante os avisos, pagamento único, recusa cosmética sem modificar dados/condição/rack, cancelamento por contrato tranquilo, retirada/recuperação e localização de botões/rótulos.

Testes de domínio cobrem limites de distância/tempo, movimento durante o aviso, cooldown, pagamento sem saldo, saída por veículo, calma, snapshots inválidos e continuidade da oscilação após save. As [evidências](../artifacts/evidence/T-F09-05/city-encounters/README.md) distinguem verificações executadas de gates visuais.

Motoristas bêbados, colisões do jogador com tráfego regular, navegação viária completa de perseguidores, queda animada, vozes finais e playthrough visual/acessível continuam pendentes. A circulação atual ainda usa o grafo de bancada do Centro Velho. O LiDAR da cidade permanece uma prévia; os encontros não transformam o gate simplificado da cidade em CQ científico completo.
