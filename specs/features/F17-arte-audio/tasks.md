# Tarefas — F17

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F17-01 — Produzir kit piloto e pipeline de importação

- [ ] Aceita

**Estado:** em revisão; [evidências](../../../artifacts/evidence/T-F17-01/README.md). Kit modular glTF 2.0 com os 9 módulos, 3 materiais de teste (`concrete`, `glass`, `water`), atlas 256×256, preset Blender e validador automático de orçamento e tags semânticas integrados; CI nativa do PR #4 pendente antes do merge.

**Marco:** M1

**Dependências:** T-F02-01

**Requisitos:** REQ-F17-01

**Cenários relacionados:** AC-F17-01

**Entregável e limite:** Módulos mínimos, atlas, preset glTF e verificador de tags/orçamento.

**Critério de aceite:** Nenhum objeto de coleta perde classe/material na exportação.

**Evidência:** Cena piloto, manifesto e relatório de triângulos/texturas.

### T-F17-02 — Produzir arte da cidade e frota completas

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F17-01, T-F02-06, T-F03-08

**Requisitos:** REQ-F17-01, REQ-F17-02

**Cenários relacionados:** AC-F17-01

**Entregável e limite:** Assets finais de quatro regiões, sete plataformas, personagens e filtros de apresentação.

**Critério de aceite:** Orçamentos e tom cartunesco são respeitados; mudanças de arte preservam IDs.

**Evidência:** Revisão visual e manifests por região.

### T-F17-03 — Produzir e integrar dublagem bilíngue

- [ ] Aceita

**Marco:** M8

**Dependências:** T-F10-06, T-F11-04

**Requisitos:** REQ-F17-03

**Cenários relacionados:** AC-F17-02

**Entregável e limite:** Gravações aprovadas por line_id, legendas, direitos de uso e importação por locale.

**Critério de aceite:** 100% das falas obrigatórias nos dois idiomas; revisão humana de pronúncia/termos.

**Evidência:** Manifest de voz, arquivos reais e checklist linguístico.

### T-F17-04 — Produzir rádios, trilha e mixagem

- [ ] Aceita

**Marco:** M8

**Dependências:** T-F17-03

**Requisitos:** REQ-F17-03, REQ-F17-04

**Cenários relacionados:** AC-F17-02, AC-F17-04

**Entregável e limite:** Três programações bilíngues, música/efeitos, volumes e ducking.

**Critério de aceite:** Duração/conteúdo mínimo atendidos e todas as fontes de áudio documentadas.

**Evidência:** Playlist, manifesto de direitos e sessão de mixagem.

### T-F17-05 — Integrar cutscenes e apresentação final

- [ ] Aceita

**Marco:** M8

**Dependências:** T-F17-02, T-F17-03, T-F10-05

**Requisitos:** REQ-F17-02, REQ-F17-05

**Cenários relacionados:** AC-F17-01, AC-F17-03

**Entregável e limite:** Abertura, atos, epílogo, cues, skip e opções visuais modernas/retrô.

**Critério de aceite:** Pular/repetir não duplica dinheiro/XP/portais; ausência de sangue e armas mantida.

**Evidência:** Playthrough com/sem skip nos dois idiomas.
