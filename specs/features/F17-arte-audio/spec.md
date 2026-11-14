# F17 — Arte, áudio, dublagem e cutscenes

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M1–M8. Origem: Direção de arte e orçamento técnico; Fun elements; Localização bilíngue (EN-US / PT-BR). Estado: especificado; não implementado.

## Requisitos

### REQ-F17-01 — Visual e pipeline

Produzir kit modular low-poly Blender→glTF 2.0, atlas, iluminação por vértice, sol/neblina e tags semânticas preservadas.

### REQ-F17-02 — Identidade cartunesca

Veículos/personagens/cidade seguem orçamentos, marcas fictícias e humor sem sangue; conforto moderno e filtro retrô opcionais.

### REQ-F17-03 — Dublagem bilíngue

Todas as falas de personagens, cutscenes e programas de rádio têm voz e legenda completas em PT-BR/EN-US na 1.0.

### REQ-F17-04 — Som e rádios

Implementar ambientes, efeitos de sensor/galinha/veículo, três rádios fictícias, música original ou licenciada e mixagem independente.

### REQ-F17-05 — Cutscenes na engine

Reproduzir cenas da campanha na engine, puláveis, com eventos de progresso idempotentes e sem depender da duração de áudio.

## Comportamento e limites

Kit mínimo de bancada: lote, calçada, esquina, cruzamento, fachada, poste, árvore, placa, bueiro e três materiais de teste. Kit completo acrescenta galpão, viaduto, túnel, costa, rocha, trilho e vegetação; cada módulo inclui colisor, LOD quando necessário, classe e material. Assets não conformes são rejeitados pelo importador/validador de manifesto.

Dublagem: `line_id`, personagem, locale, arquivo, texto da legenda, duração e revisão do roteiro. Catálogo cobre proposta, dicas, retrabalho, êxito/falha, NPCs interativos, cutscenes e rádio. Falas interrompidas por troca de idioma terminam na língua atual; a próxima usa a selecionada. Legendas têm identificação de falante e eventos sonoros relevantes. Silêncio de fallback não satisfaz o aceite da 1.0.

Rádios: Geodésia FM, Rádio Multicaminho e Onda Portadora; cada uma recebe ao menos 10 min de programação por idioma, com duas músicas/peças instrumentais e um talk show roteirizado. Música pode ser compartilhada se instrumental/licenciada; apresentação e talk show são adaptados e dublados. Piadas de terraplanismo são sátira, com contraponto pedagógico no contexto.

Mixagem separa música, vozes e efeitos, com controles 0–100 e ducking de rádio durante instruções. Efeitos incluem cacarejo proporcional ao estresse, falha GNSS, descarga, buzina, chuva e tráfego. Cutscenes mínimas: abertura, fechamento de cada ato e epílogo; cues de missão menores usam diálogo em engine. Filtro retrô pontilhado é opcional e desligado por padrão.

## Interfaces

AssetManifest, DialogueEntry, VoiceManifest, RadioProgramme, CutsceneCue e semantic metadata glTF. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F17-01

Dado asset exportado, então escala, colisão, classe/material e orçamento estão corretos após importação; nomes de fabricantes reais não aparecem.

### AC-F17-02

Dado catálogo de falas obrigatório, então cada ID tem áudio/legenda aprovados nos dois idiomas e a duração não corta texto ou progresso.

### AC-F17-03

Dada cutscene pulada/repetida, então a campanha progride uma vez e objetivos continuam legíveis; áudio respeita volume e locale.

### AC-F17-04

Dada rádio/efeito, então reprodução funciona offline e todo arquivo possui origem/licença/autor registrado.
