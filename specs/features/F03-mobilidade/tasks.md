# Tarefas — F03

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F03-01 — Implementar locomoção inicial e entradas

- [ ] Aceita

**Marco:** M1

**Dependências:** T-F01-02, T-F02-01

**Requisitos:** REQ-F03-01, REQ-F03-03, REQ-F03-04

**Cenários relacionados:** AC-F03-01, AC-F03-04

**Entregável e limite:** Andar a pé, carrinho, colisão básica, interação e mapa de ações teclado/gamepad.

**Critério de aceite:** Percurso com subida, curva e obstáculo é controlável; remapeamento persiste.

**Evidência:** Cena de teste e roteiro de controles.

### T-F03-02 — Integrar galinha e estresse

- [ ] Aceita

**Marco:** M1

**Dependências:** T-F03-01, T-F04-02

**Requisitos:** REQ-F03-02

**Cenários relacionados:** AC-F03-02

**Entregável e limite:** Estabilização visual/física observada, eventos de susto, milho, descanso e fuga segura.

**Critério de aceite:** Os limiares de parâmetros alteram a câmera e a galinha nunca recebe ferimento.

**Evidência:** Replay calmo/estressado e teste do medidor.

### T-F03-03 — Adicionar bicicleta e recursos

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F03-02, T-F08-02

**Requisitos:** REQ-F03-01, REQ-F03-02, REQ-F03-04

**Cenários relacionados:** AC-F03-01, AC-F03-04

**Entregável e limite:** Perfil bicicleta, acessos estreitos, autonomia e medidores de recurso.

**Critério de aceite:** Compra/desbloqueio corretos e captura para antes de exceder a capacidade.

**Evidência:** Percurso de ciclovia e teste de esgotamento.

### T-F03-04 — Adicionar motoneta e recuperação

- [ ] Aceita

**Marco:** M3

**Dependências:** T-F03-03, T-F12-02

**Requisitos:** REQ-F03-01, REQ-F03-03, REQ-F03-04

**Cenários relacionados:** AC-F03-01, AC-F03-03

**Entregável e limite:** Motoneta em ruas, recuperação de acidente e integração de perda de lote.

**Critério de aceite:** Câmeras podem ser montadas frente/trás; acidente preserva lotes protegidos.

**Evidência:** Save antes/depois de acidente e percurso.

### T-F03-05 — Implementar carro com suspensão

- [ ] Aceita

**Marco:** M4

**Dependências:** T-F03-04

**Requisitos:** REQ-F03-01, REQ-F03-03

**Cenários relacionados:** AC-F03-01, AC-F03-03

**Entregável e limite:** Controlador de quatro rodas por raios, carro e eventos de impacto para rack.

**Critério de aceite:** Frenagem, lombada e curva são estáveis; impacto emite um evento de Δv.

**Evidência:** Percurso automotivo e log de impactos.

### T-F03-06 — Adicionar Kombi

- [ ] Aceita

**Marco:** M5

**Dependências:** T-F03-05, T-F08-04

**Requisitos:** REQ-F03-01, REQ-F03-04

**Cenários relacionados:** AC-F03-01, AC-F03-04

**Entregável e limite:** Perfil Kombi e slots/energia/armazenamento para LiDAR, DMI e computador.

**Critério de aceite:** Contrato de túnel cabe na autonomia com planejamento e descarga.

**Evidência:** Percurso e orçamento de recursos.

### T-F03-07 — Adicionar picape 4x4

- [ ] Aceita

**Marco:** M6

**Dependências:** T-F03-06, T-F02-05

**Requisitos:** REQ-F03-01, REQ-F03-03

**Cenários relacionados:** AC-F03-01, AC-F03-03

**Entregável e limite:** Tração/aderência de terra, inclinação e perfil com dois LiDARs.

**Critério de aceite:** Consegue rota da Serra sem atravessar obstáculos nem ignorar aderência.

**Evidência:** Replay seco/molhado na encosta.

### T-F03-08 — Adicionar van MMS

- [ ] Aceita

**Marco:** M7

**Dependências:** T-F03-07

**Requisitos:** REQ-F03-01, REQ-F03-02, REQ-F03-04

**Cenários relacionados:** AC-F03-01, AC-F03-04

**Entregável e limite:** Perfil de engenharia, câmeras de pavimento e mascote no painel.

**Critério de aceite:** Configuração completa respeita slots, peso de jogo e recursos sem remover plataformas anteriores.

**Evidência:** Inspeção da frota e percurso instrumentado.
