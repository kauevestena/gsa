# Tarefas — F21

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F21-01 — Implementar mochila e levantamento interior

- [ ] Aceita

**Marco:** E2

**Dependências:** T-F19-05

**Requisitos:** REQ-F21-01

**Cenários relacionados:** AC-F21-01

**Entregável e limite:** Perfil, montagem a pé, cenário de estação e controles locais.

**Critério de aceite:** Coleta sem GNSS usa dados permitidos e respeita energia/armazenamento.

**Evidência:** Replay interior e CQ com/sem controle.

### T-F21-02 — Implementar veículo rodoferroviário

- [ ] Aceita

**Marco:** E2

**Dependências:** T-F21-01

**Requisitos:** REQ-F21-02

**Cenários relacionados:** AC-F21-02

**Entregável e limite:** Controlador de trilho, pontos de conversão e grade de circulação.

**Critério de aceite:** Janelas evitam colisão inevitável e DMI/estimativa permanecem consistentes.

**Evidência:** Cenários de janela aberta/ocupada e save.

### T-F21-03 — Implementar barco e margens

- [ ] Aceita

**Marco:** E2

**Dependências:** T-F21-01

**Requisitos:** REQ-F21-03

**Cenários relacionados:** AC-F21-03

**Entregável e limite:** Flutuação simplificada, sensores, materiais e rota de margem.

**Critério de aceite:** Ondas produzem atitude/erro observável e água não gera falsa cobertura.

**Evidência:** Replay em água calma/agitada e relatório de margem.

### T-F21-04 — Entregar missões e produção da expansão

- [ ] Aceita

**Marco:** E2

**Dependências:** T-F21-02, T-F21-03

**Requisitos:** REQ-F21-04

**Cenários relacionados:** AC-F21-04

**Entregável e limite:** Três tutoriais/missões, dois contratos derivados por plataforma, traduções, vozes e revisões.

**Critério de aceite:** Todas as plataformas têm caminho completo de coleta/CQ/pagamento nos dois modos/idiomas.

**Evidência:** Playthroughs, manifests e validação procedural.
