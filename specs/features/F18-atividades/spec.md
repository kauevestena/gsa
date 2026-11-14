# F18 — Atividades, colecionáveis e humor

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M3–M8. Origem: Fun elements; Loop de jogo e estrutura; Mundo. Estado: especificado; não implementado.

## Requisitos

### REQ-F18-01 — Coleções pedagógicas

Oferecer ovos, oito marcos e álbum de cartas de projeções, com desbloqueios/recompensas persistentes.

### REQ-F18-02 — Bloopers e personalização

Capturar momentos humorísticos e permitir fantasias, pinturas, adesivos e buzinas, sem alterar erro científico por cosmético.

### REQ-F18-03 — Eventos de cidade

Trio elétrico, maratona e pombos criam obstáculos temporários sinalizados e rotas alternativas, respeitando contratos tranquilos.

### REQ-F18-04 — Desafios e filtros

Implementar Desafio da Galinha, filtro Daguerreótipo e modo cidade em nuvem de pontos liberado ao terminar a campanha.

## Comportamento e limites

Ovos: até cinco ativos no Centro por dia, posições sorteadas em pontos acessíveis, 10 Coords cada e ID composto por dia/slot. Marcos são oito, com ocupação de 5 s e verbete histórico. Cartas: Mercator, Robinson, Gall-Peters, UTM, Lambert e estereográfica, em seis locais fixos, cada uma com aplicação e distorção explicadas. Colecionável não some definitivamente se aparecer em área temporariamente bloqueada.

Álbum de Bloopers guarda até 30 miniaturas de NPC careta, pombo na lente e selfie de Gimbalina; confirmação permite substituir antigas. Máscaras de privacidade também se aplicam a imagens do álbum quando compartilhadas/exportadas. Não oferecer compartilhamento online na 1.0. Fantasias: capacete, colete e óculos; frota: três cores, três adesivos e três buzinas, incluindo cacarejo, por plataforma.

Eventos duram até 2 min físicos e só começam se existir desvio transitável no grafo. Em contratos tranquilos, usar apenas versões ambientais sem dano/ameaça; Cidade calma mantém espetáculo visual, mas remove perseguição/estresse hostil. Densidade de pombos pode afetar observações reais e o filtro posterior.

Desafio da Galinha: pista de 200 m, medir tempo físico e percentagem de amostras com cabeça dentro de ±5° da orientação desejada; sucesso ≥95%, medalha por tempo, sem pagamento de contrato falso. Daguerreótipo é pós-efeito sépia sem alterar miniaturas científicas/QC. Modo Nuvem de Pontos renderiza amostragem visual da cena, separado da nuvem coletada e sinalizado como filtro.

## Interfaces

CollectibleState, AlbumEntry, CosmeticDefinition, WorldEvent e ChallengeResult; desbloqueios por campanha. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F18-01

Dado colecionável já obtido e save recarregado, então recompensa/verbete não se duplicam e itens restantes continuam alcançáveis.

### AC-F18-02

Dado cosmético ou filtro ativado, então dados científicos/CQ são invariantes para mesma aquisição e a alteração é apenas apresentação.

### AC-F18-03

Dado evento urbano, então há aviso e desvio, termina no prazo e respeita contrato tranquilo/Cidade calma.

### AC-F18-04

Dado desafio com cabeça fora da tolerância ou campanha incompleta, então medalha/filtro final não são concedidos indevidamente.
