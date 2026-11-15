# Tarefas — F01

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. O campo Estado registra o andamento de cada incremento iniciado. O aceite de código segue a revisão delegada registrada em specs/README.md; não marcar por conclusão da documentação.

### T-F01-01 — Fixar ferramentas e bootstrap

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #1 após CI Linux/Windows verde no SHA de773cd8227994cc9da18a36d352d02a9654c583. Critério local verificado; registros históricos e pendências de inspeção visual/isolamento Windows preservados em artifacts/evidence/M0-ci/summary.json.

**Marco:** M0

**Dependências:** Nenhuma

**Requisitos:** REQ-F01-01

**Cenários relacionados:** AC-F01-01

**Entregável e limite:** Registrar versões, hashes, comandos de instalação e export; iniciar Git local e ignores.

**Critério de aceite:** Build mínimo e export nas duas plataformas; ausência de ambiente deve permanecer registrada como pendência, não como sucesso.

**Evidência:** Manifest de toolchain e logs por OS.

### T-F01-02 — Criar módulos e testes mínimos

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #1 após CI Linux/Windows verde no SHA de773cd8227994cc9da18a36d352d02a9654c583. Critério local verificado; registros históricos e pendências de inspeção visual/isolamento Windows preservados em artifacts/evidence/M0-ci/summary.json.

**Marco:** M0

**Dependências:** T-F01-01

**Requisitos:** REQ-F01-02

**Cenários relacionados:** AC-F01-03

**Entregável e limite:** Criar solução, referências unidirecionais, fixture de transformação e fachada mínima.

**Critério de aceite:** Core testável sem Godot; teste significativo de conversão ENU passa.

**Evidência:** Grafo de dependências e dotnet test.

### T-F01-03 — Implementar validação de conteúdo

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #1 após CI Linux/Windows verde no SHA de773cd8227994cc9da18a36d352d02a9654c583. Critério local verificado; registros históricos e pendências de inspeção visual/isolamento Windows preservados em artifacts/evidence/M0-ci/summary.json.

**Marco:** M0

**Dependências:** T-F01-02, T-F06-01

**Requisitos:** REQ-F01-03

**Cenários relacionados:** AC-F01-02

**Entregável e limite:** CLI e validadores para contratos, perfis, regiões e missões conforme interfaces.

**Critério de aceite:** Arquivos válidos passam; referências quebradas, NaN, polígonos degenerados e excelente incoerente falham.

**Evidência:** Fixtures válidos/inválidos e saída JSON.

### T-F01-04 — Automatizar builds e exports

- [x] Aceita

**Estado:** aceita tecnicamente pela revisão delegada; integrada no PR #1 após CI Linux/Windows verde no SHA de773cd8227994cc9da18a36d352d02a9654c583. Critério local verificado; registros históricos e pendências de inspeção visual/isolamento Windows preservados em artifacts/evidence/M0-ci/summary.json.

**Marco:** M0

**Dependências:** T-F01-02, T-F01-03

**Requisitos:** REQ-F01-04

**Cenários relacionados:** AC-F01-01

**Entregável e limite:** Scripts locais por OS com restore/build/test/export e diretório de artefatos.

**Critério de aceite:** Repetição com mesmo lock gera executáveis funcionais sem depender de servidor em runtime.

**Evidência:** Logs e smoke test dos dois executáveis.
