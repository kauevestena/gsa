# Perigos na cidade: primeira integração jogável

A cidade conecta atenção territorial a efeitos físicos, fotos e custódia. Este incremento usa os modelos de F09/F16 já existentes e não encerra o playtest de T-F16-04.

## Como jogar

A partir do carro, a Geopédia libera atenção territorial. Fotografar gera atividade por 0,6 s; manter a prévia LiDAR ligada na Kombi ou plataforma superior gera atividade contínua. Atenção cresce 1 ponto/s físico de atividade, com os limiares 20/40/60/80/100, aviso mínimo de 3 s e cooldown de 30 s. Confira territórios e estado no mapa/aba Técnico.

| Evento | Consequência nesta versão | Saída disponível |
| --- | --- | --- |
| Nível 1 | Aviso textual e sonoro | Interromper coleta ou sair do território |
| Nível 2 — Máfia/Cartel/MapaFácil | Tinta acrescenta 45% de obstrução; fotos posteriores falham na qualidade | Parar e segurar E, ou pedir limpeza no tablet, durante 3 s; fotografar novamente |
| Nível 2 — Terra Plana | Viés GNSS cresce até 10 m em 5 s, durante até 20 s | Sair do território/zona segura; examinar inovação e incerteza |
| Nível 2 — Marco Torto | Marco físico próximo desloca 0,8 m E / −0,3 m N; coordenadas publicadas permanecem | Conferir fisicamente e usar referência independente |
| Nível 3 — MapaFácil | Pneu limita movimento real a 6 m/s | Parar e reparar por 50 C$ no tablet |
| Nível 3 — outras facções | Perseguidor por até 20 s; contato causa impacto/dano no carro e rack | Sair do território ou alcançar zona segura |
| Nível 4 | Barreira sólida 22 m à frente por até 30 s e perseguidor | Desviar, sair ou chegar à zona segura |
| Nível 5, parado por 15 s | Bomba com contagem de 10 s; veículo e fotos a bordo podem ser perdidos | Sair do território ou desarmar parado por 5 s (E/tablet) |

Os tempos são físicos, sem o avanço de 60× do calendário. O disfarce reduz o ganho, sem alongar avisos, cooldowns ou contagens. A bomba não mostra ferimentos no protagonista ou na Gimbalina.

## Dados e recuperação

Na aba **Escritório**, envie fotos perto de `office_entry`, descarga ou Wi-Fi (raio de 10 m). O envio cria um lote com checksum no `BatchCustodyStore`, protege-o e libera armazenamento virtual. Fotos contaminadas continuam contaminadas depois do envio/limpeza. Mover um marco não reescreve aquisições anteriores.

Perda do veículo apaga fotos pendentes e nuvem visual, conserva lotes protegidos e devolve o jogador ao escritório com carrinho gratuito. O veículo perdido permanece indisponível. A reposição exige preço integral ou franquia do seguro daquele veículo; não recupera observações perdidas. Danos de colisão são conservados por plataforma ao trocar/salvar/carregar, incluindo múltiplos veículos perdidos.

Seguro custa 2% do veículo; franquia de 10%, para aquele veículo, durante o trabalho atual. Escolta custa 10% do contrato; disfarce da Kombi custa 200 C$; ambos reduzem atenção em 50% na zona escolhida. Autorização comunitária custa 5% e exige Negociação 1 neste primeiro incremento; suspende ameaças naquela zona/trabalho. Reduções têm piso de 25%, exceto autorização. Compras repetidas de proteção ativa não cobram novamente. Abandonar/concluir encerra proteções/seguro; marcas deslocadas e danos físicos permanecem.

Aceitar outro trabalho exige concluir/abandonar o atual. O abandono permite continuar com carrinho ou plataforma disponível quando falta saldo para reposição.

## Proteção tranquila

Cidade calma **ou** contrato `Calm` cancela avisos/bomba, spoofing, barreira e perseguidor sem cobrar contramedidas. Escritório/descarga/Wi-Fi e outras áreas protegidas fazem o mesmo localmente. Isso não limpa lentes, repara danos anteriores ou apaga multas de trânsito. Desligar Cidade calma não remove a proteção de um contrato tranquilo já aceito.

## Navegação, qualidade e limites

Fotos de carro/Kombi/picape/van guardam pose estimada de GNSS/IMU/DMI; carro/Kombi precisam mover-se para inicializar heading. Interferência afeta a observação; o filtro aceita/rejeita por inovação, sem consultar verdade. O painel Técnico avisa quando uma correção é rejeitada. Inconsistência não comprova spoofing: viés lento pode ser absorvido, e fixes iniciais já enviesados também podem não ser detectados.

Envie as imagens em um ponto de descarga e selecione **Processar e entregar** no Escritório. A revisão verifica os alvos obrigatórios, a proporção de imagens válidas, a privacidade e o formato. Em caso de retrabalho, os lotes protegidos entram na revisão seguinte junto com as fotos novas. O pagamento segue o laudo e o prazo do contrato.

| Missão | Coleta, controle de qualidade e progresso |
| --- | --- |
| HIST-01 · Fachadas | Fotografe as 20 fachadas marcadas no mapa. Cubra pelo menos 90% da área útil das faces sul em células de 2 m. A câmera verifica o enquadramento e a visibilidade de cada célula. O Escritório indica as superfícies a revisitar. |
| HIST-02 · Drenagem | Fotografe cada um dos 12 bueiros de posições separadas. A chuva chega após três horas no relógio do jogo e afeta a qualidade das fotos. O Escritório triangula as posições e exige RMSE absoluto de até 2 m. A entrega libera a compra da bicicleta. |
| HIST-03 · Calçadas | Equipe a bicicleta. Enquadre os marcos azuis de controle e os alvos de medição em duas posições separadas por trecho. O Escritório usa as coordenadas publicadas dos marcos azuis para ajustar a translação da câmera e triangula separadamente os alvos de medição. Exige RMSE combinado de até 0,20 m para largura e altura da guia. A entrega libera a compra da motoneta. |
| HIST-04 · Placas | Equipe a motoneta e fotografe as 12 placas da avenida em duas passadas. O Escritório exige RMSE de até 0,15 m nas distâncias entre placas vizinhas. A entrega fecha o Ato 1 e libera o Porto, a Orla e a compra do carro. |
| HIST-05 · Corredor de Ônibus | Com o carro, siga a faixa marcada no Porto e na Orla das 10h às 16h. Faça panoramas de seis faces de posições separadas após inicializar a navegação. Descarregue as fotos das seis placas azuis. O Escritório exige inventário completo e RMSE absoluto de até 6 m para este circuito autônomo. A entrega abre a compra da Kombi com reputação 20 na prefeitura. |

O quadro oferece cinco contratos procedurais com duas rotas por modelo: fachadas, drenagem, calçadas, placas e parque. As ofertas usam os alvos da rota selecionada e os requisitos da frota montada. O contrato tranquilo do parque está disponível com o carrinho e sem taxa de entrada. Salvamentos preservam fotos, evidência de cobertura e desbloqueios.

O ajuste das fotos de calçadas mantém a orientação da câmera fixa e corrige o erro de posição simulado a partir dos marcos visíveis. As outras câmeras leves ainda usam poses ideais. O corredor de ônibus guarda as seis faces de cada panorama no lote. No CQ do inventário, o Escritório avalia a qualidade das faces que observaram alvos do contrato e reconstrói posições com as poses estimadas. O prazo recomendado é de 30 h e o final de 36 h, permitindo aguardar a próxima abertura da faixa. Os contratos procedurais fornecem renda adicional para comprar veículos.

As missões a partir da HIST-06, entregas de nuvens e perfis, Base/PPK e logs persistentes de navegação continuam em desenvolvimento. A prévia LiDAR segue disponível para exploração.

O circuito de HIST-05 valida seis alvos de inventário. A cobertura contínua do corredor e o limite de 0,5 m do produto 360° de [parâmetros](../specs/parametros.md#sensores-e-qualidade) seguem pendentes da integração de controle ou posicionamento corrigido. O limite provisório reflete a navegação autônoma disponível na cena.

Perseguidor e barreira são representações básicas. O perseguidor avança para o jogador respeitando obstáculos locais, sem planejamento viário completo. Pneu/lente ainda são estados do conjunto de aquisição compartilhados entre plataformas. Arte, colocação de barreiras nas ruas, acessibilidade e balanceamento precisam de inspeção visual/playtest. Os [encontros de cães e estacionamento](city-everyday-encounters.md) agora têm integração própria. A [circulação do Centro Velho](old-center-traffic.md) integra colisões, motoristas erráticos e blitz. Concorrência narrativa da MapaFácil, patrulhas por horário e playthrough final simultâneo das cinco facções também continuam pendentes.

## Verificação

`python3 scripts/verify.py --exports host` exige `GSA_CITY_HAZARDS_SMOKE_OK` na cena e executável exportado: callbacks reais de câmera/upload/serviços, warning→tinta, limpeza interrompida, movimento com pneu, colisão com barreira, marco deslocado, ambiente GNSS real/pose estimada, rejeição de salto observado, bomba retomada de save, perda/seguro e contrato tranquilo com Cidade calma desligada. O slot temporário é separado do fixture original; não é playthrough humano/inspeção visual.

O smoke de entrega também executa HIST-01 a HIST-05 e as dez rotas procedurais. Exercita cobertura incompleta, ajuste por controle observado, compra do carro com a renda dos contratos, horários da faixa, poses observadas, recarga de panoramas, expiração, liquidação tardia sem repetição e acesso à Kombi.

O save permanece envelope v2 com catálogo 4 para incluir encontros/estresse; builds anteriores rejeitam esse catálogo para evitar apagar o novo estado. Ver [compatibilidade](save-compatibility.md) e [evidência](../artifacts/evidence/T-F16-04/city-hazards/README.md).
