# F11 — Interface, localização e Geopédia

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M0–M8. Origem: Público e objetivos pedagógicos; Direção de arte e orçamento técnico; Localização bilíngue (EN-US / PT-BR). Estado: T-F11-01 a T-F11-04 aceitas tecnicamente; catálogo avançado/tablet e navegação diagnóstica T-F11-05 em revisão. Inspeção visual/controle e revisão final continuam pendentes.

## Requisitos

### REQ-F11-01 — Localização desde o início

Textos do jogo usam chaves e PO PT-BR/EN-US, troca em runtime, plural/contexto e glossário CSV versionado.

### REQ-F11-02 — HUD e planejamento

Exibir radar circular/cobertura, GNSS, sensores, recursos, estresse, dinheiro e atenção quando aplicável; mapa de céu/rotas e quadro de contratos.

### REQ-F11-03 — CQ legível

Arcade mostra estrelas e ação corretiva; Técnico mostra unidades/valores/limites e parâmetros, sem alterar exigências.

### REQ-F11-04 — Geopédia

Desbloquear verbetes bilíngues por conceito e marcos, conectando mecânica, modelo e limitações didáticas.

### REQ-F11-05 — Acessibilidade de apresentação

Oferecer legendas, escala de interface, controles remapeáveis, indicadores além de cor e números locais com unidades métricas.

## Comportamento e limites

Locale inicial usa idioma do sistema: português → pt-BR; inglês ou não suportado → en-US. Escolha explícita persiste. Troca atualiza HUD/menu/mapa/Geopédia/legendas e seleciona voz no próximo enunciado, sem alterar dados ou relógio. Formatação de UI é por cultura; arquivos de simulação/JSON usam ponto decimal. Não concatenar substantivos para plural/gênero; escrever mensagens completas contextualizadas.

HUD oculta sistemas ainda não disponíveis (atenção antes de facções, LiDAR antes de nível 4), sem exibir falsos sensores. Cobertura de campo é provisória e rotulada; CQ oficial usa produto processado e pode divergir. Cores têm ícones/padrões complementares. No mapa, feições faltantes e trechos degradados permitem traçar até 10 waypoints sem autopilotar a coleta.

Geopédia inicial: cobertura, GNSS, sobreposição, exposição/arrasto, controle/checagem, privacidade, datum/projeção e alturas. Completar com lever arm, boresight, sincronização, IMU/INS, DMI/ZUPT, base/DGNSS/RTK/PPK, LiDAR/densidade/oclusão/retornos, RMSE, calibração de câmera e geodésia histórica. Cada verbete tem exemplo do jogo, unidade/efeito e limite do modelo. PT-BR e EN-US usam o mesmo glossário técnico, humor adaptado.

Glossário CSV tem `term_id,pt_BR,en_US,definition_pt_BR,definition_en_US,usage_note`. A forma escolhida para cada termo técnico deve aparecer na UI, nos roteiros e na voz.

## Interfaces

LocalizationKey, HUDSnapshot, MapOverlay, QualityReportView e GeopediaEntry; binding pela fachada. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F11-01

Dada qualquer tela e um idioma selecionado, quando trocar PT-BR/EN-US, então texto, plural, números e próxima fala mudam sem alterar save científico.

### AC-F11-02

Dado sensor degradado ou cobertura insuficiente, então HUD/mapa distinguem status e CQ provisório/final por texto/ícone além da cor.

### AC-F11-03

Dada falha no CQ, então Arcade indica ação corretiva e Técnico mostra valor/unidade/limite do mesmo relatório.

### AC-F11-04

Dado conceito ou marco desbloqueado, então o verbete correto aparece nos dois idiomas e permanece após save; UI ampliada não corta comandos essenciais.
