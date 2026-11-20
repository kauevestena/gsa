# F16 — Facções, atenção e negociação

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M4–M7. Origem: Perigos e antagonistas; Missões; Loop de jogo e estrutura. Estado: domínio integrado; primeira integração jogável em validação. Playthrough completo e revisão visual pendentes.

## Requisitos

### REQ-F16-01 — Facções fictícias

Implementar cinco facções com territórios, motivos e ações próprias, sem armas pelo jogador nem associação a comunidades reais.

### REQ-F16-02 — Atenção graduada

Mapear território aumenta atenção em cinco níveis com avisos e escalada observável de olheiros até bomba estacionada.

### REQ-F16-03 — Sabotagem de dados

Mover marcos, obstruir lente, furar pneu, falsear GNSS, perseguir e bloquear alteram observações/veículo conforme evento.

### REQ-F16-04 — Contramedidas

Permitir rota/horário alternativo, limpeza, seguro, upload frequente, escolta, disfarce e autorização por negociação.

### REQ-F16-05 — Compatibilidade calma

Cidade calma cancela ameaças pendentes e usa variantes de missão preservando produto, prazos-base e progressão.

## Comportamento e limites

Facções e especialidades: Sindicato do Marco Torto move marcos; Cartel do Cimento protege obra clandestina; Máfia do Mapa Velho organiza bloqueios; Sociedade da Terra Plana produz spoofing GNSS/cartazes na lente; MapaFácil S/A sabota pneus e disputa contratos por narrativa. Todas podem participar da pressão final, sem inimigos armados controlados pelo jogador.

Atenção por território acumula 1 ponto/s físico de coleta ativa, reduz 2 pontos/s fora do território ou sem coleta por 10 s; limiares 20/40/60/80/100. Níveis: 1 olheiro/aviso; 2 tinta; 3 pneu/perseguição; 4 bloqueio/perseguição; 5 tentativa de bomba em veículo parado. Cooldown de ação por território: 30 s. Aviso audiovisual precede ataque por ≥3 s. Bomba requer estacionado por 15 s, aviso de 10 s e desaparece ao sair do território ou ativar Cidade calma; desarmar parado por interação de 5 s também resolve.

Spoofing injeta deslocamento progressivo de até 10 m em 5 s; detector de inovação da fusão sinaliza inconsistente acima de 3 sigma. Rejeitar observação suspeita usa essa evidência, não verdade. Marcos movidos preservam referência de avaliação e o histórico de aquisição. Tinta/pneu respeitam eventos persistidos; save não repete ataque já resolvido.

Escolta reduz taxa de atenção pela metade; disfarce reduz 50% até primeiro ataque; autorização suspende atenção no território daquele contrato; reduções multiplicativas acumuladas têm piso de 25% da taxa original. Negociação exige nível de habilidade correspondente à hostilidade contratual. Cidade calma suprime a escalada independentemente dessas compras.

A autorização é negociada com a associação do bairro, apresentada como interlocutora comunitária legítima e distinta das facções fictícias. O disfarce inicial é um adesivo de pizzaria na Kombi; coleta de madrugada também permite evitar horários de patrulha previstos no contrato.

## Interfaces

FactionDefinition, TerritoryState, AttentionState, HazardEvent e ProtectionService; injeção de observação GNSS. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F16-01

Dada coleta em território, então limiares produzem avisos e ações em ordem; sair permite decair e não há ataque instantâneo invisível.

### AC-F16-02

Dado spoofing ou marco movido, então trajetória/levantamento apresentam erro detectável por observações e checagem, sem correção por oracle.

### AC-F16-03

Dada bomba com dados pendentes/protegidos, então ação/contramedida e perda obedecem à custódia; galinha/protagonista não exibem ferimentos.

### AC-F16-04

Dada Cidade calma ou autorização válida, então eventos hostis são cancelados/suspensos conforme política e missão continua concluível.

## Integração jogável incremental

A [matriz de cidade](../../../docs/city-hazards.md) delimita efeitos por facção/nível, captura/custódia, pose estimada e proteção por contrato. Spoofing/marco substituem a tinta no nível 2 dessas facções; Cartel/Máfia/MapaFácil usam tinta. Os demais encontros originais e a pressão simultânea final continuam pendentes. Testes de domínio anteriores não demonstram playthrough visual.
