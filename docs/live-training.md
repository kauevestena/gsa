# Treinamento jogável: três fachadas

Este incremento conecta sistemas existentes a ações do jogador na cena Godot. Não fecha os gates de playtest M2/M3, nem torna a cidade M7 ou a campanha inteira jogáveis.

## Jogar

1. Abra o jogo, selecione Português ou English e clique em **Explorar o quarteirão**.
2. O carrinho começa perto do escritório e da descarga. Aceite o serviço no painel.
3. Vá até as fachadas **18, 19 e 20**, numeradas em amarelo. W move, S freia e A/D viram. É possível virar parado. C fotografa no próximo instante de aquisição (até 0,5 s). V alterna a vista aérea próxima e a câmera de coleta.
4. Pare e aponte a câmera para a fachada. Fotografe de duas posições separadas lateralmente por pelo menos 1 m. Uma única foto ou duas fotos da mesma posição não resolvem a profundidade. O painel mostra os alvos vistos na última foto e ✓ quando há triangulação confirmada.
5. Volte à **Descarga**, aproxime-se a até 5 m e descarregue. O escritório permite ações a até 8 m, medidas na horizontal.
6. Processe no escritório. A ação pula 10 minutos de calendário e cobra a taxa assistida (normalmente 8 Coords; o subsídio de baixo saldo continua aplicável). Nenhuma foto de campo pode ficar pendente.
7. Se o CQ pedir recoleta, obtenha novas vistas das fachadas ainda pendentes, descarregue e reprocesse. Completude, privacidade e formato precisam passar.
8. Entregue para receber até 150 Coords e 25 XP. A liquidação usa as mesmas proteções de idempotência do domínio.

F5 salva; F9 carrega. Aceite, descarga, processamento e pagamento também salvam. Entrar novamente na cena restaura o treino e reposiciona o carrinho junto ao escritório. O painel tem rolagem em janelas pequenas. Escape salva e retorna ao menu.

## Escopo e limites

- Treinamento em desenvolvimento, com **poses ideais**, clima seco e imagens sintéticas do simulador. Não há alegação de erro GNSS realista neste modo. A câmera usa a posição/orientação atual do carrinho, velocidade e rotação para exposição/rolling shutter, e raycasts da física Godot para oclusão.
- Reconstrução usa somente pixels, poses estimadas e calibração de `CameraObservationSample`, pelo `FeatureInventoryTriangulator`. Coordenadas dos alvos não são copiadas para o inventário.
- Até 200 fotos por sessão; pedidos manuais C/botão, limitados pelo relógio de 2 Hz. Os buffers duplicados do scheduler são liberados após transferir a observação para a custódia. As miniaturas são sintéticas e não capturas do framebuffer.
- O slot separado `user://training-live` não altera saves de carreira/HIST. Ele usa os envelopes com checksum e rotação do `SaveGameManager`. Um carregamento corrompido/incompatível bloqueia novas gravações até resolver o arquivo ou carregar um save válido.
- Processamento é uma ação síncrona no escritório; não há job de treino em andamento para persistir. Após carregar, o CQ é recalculado dos lotes protegidos sem cobrar processamento novamente. Observações ainda em campo não entram na entrega.
- O exercício não constitui as missões HIST-01..11. A cidade completa, frota avançada, áudio final e avaliação humana permanecem trabalhos separados.

## Verificação

`LiveTrainingContractTests` cobre pré-condições de distância, profundidade sem paralaxe, oclusão, fotos duplicadas, preservação de lotes no save, recoleta, processamento, integridade dos dados brutos e pagamento idempotente após carregar.

O smoke da cena também executa `VerifyPlayableTraining` no contexto real de física Godot: aciona os mesmos callbacks dos botões, fotografa uma vista vazia, exige retrabalho, reposiciona o carrinho em seis posições de teste, fotografa com raycasts reais, descarrega, processa, salva/carrega e entrega duas vezes sem duplicar pagamento. O smoke reposiciona o jogador para ser determinístico; não testa uma pessoa navegando com teclado.

O marcador obrigatório `GSA_PLAYABLE_TRAINING_SMOKE_OK` é verificado pelo pipeline tanto na cena quanto no executável nativo exportado. Isso não substitui inspeção gráfica ou playtest humano.
