# Tarefas — F01

[Especificação](spec.md) · [Plano](plan.md) · [Fluxo SDD](../../README.md)

Estado inicial de todas as tarefas: pendente. O campo Estado registra o andamento de cada incremento iniciado. Marcar a caixa somente após evidência e revisão humana; não marcar por conclusão da documentação.

### T-F01-01 — Fixar ferramentas e bootstrap

- [ ] Aceita

**Estado:** em revisão; [evidências](../../../artifacts/evidence/T-F01-01/README.md). Build, exports Windows/Linux e smoke Linux headless offline passaram; execução nativa Windows, verificação gráfica e revisão humana pendentes. A caixa permanece desmarcada.

**Marco:** M0

**Dependências:** Nenhuma

**Requisitos:** REQ-F01-01

**Cenários relacionados:** AC-F01-01

**Entregável e limite:** Registrar versões, hashes, comandos de instalação e export; iniciar Git local e ignores.

**Critério de aceite:** Build mínimo e export nas duas plataformas; ausência de ambiente deve permanecer registrada como pendência, não como sucesso.

**Evidência:** Manifest de toolchain e logs por OS.

### T-F01-02 — Criar módulos e testes mínimos

- [ ] Aceita

**Estado:** em revisão; [evidências](../../../artifacts/evidence/T-F01-02/README.md). Core independente e fachada C#/GDScript verificados, com 142 testes .NET na integração M0. Nenhum aceite de plataforma foi presumido.

**Marco:** M0

**Dependências:** T-F01-01

**Requisitos:** REQ-F01-02

**Cenários relacionados:** AC-F01-03

**Entregável e limite:** Criar solução, referências unidirecionais, fixture de transformação e fachada mínima.

**Critério de aceite:** Core testável sem Godot; teste significativo de conversão ENU passa.

**Evidência:** Grafo de dependências e dotnet test.

### T-F01-03 — Implementar validação de conteúdo

- [ ] Aceita

**Estado:** em revisão; [evidências](../../../artifacts/evidence/T-F01-03/README.md). Conteúdo válido retorna 0; referência quebrada retorna 2 e identifica arquivo/campo. Casos negativos e quatro produtos cobertos por testes.

**Marco:** M0

**Dependências:** T-F01-02, T-F06-01

**Requisitos:** REQ-F01-03

**Cenários relacionados:** AC-F01-02

**Entregável e limite:** CLI e validadores para contratos, perfis, regiões e missões conforme interfaces.

**Critério de aceite:** Arquivos válidos passam; referências quebradas, NaN, polígonos degenerados e excelente incoerente falham.

**Evidência:** Fixtures válidos/inválidos e saída JSON.

### T-F01-04 — Automatizar builds e exports

- [ ] Aceita

**Estado:** em revisão; [evidências](../../../artifacts/evidence/T-F01-04/README.md). Pipeline portátil passou em Linux, com exports dos dois sistemas e smoke Linux headless offline. Execução nativa Windows, inspeção gráfica e revisão humana pendentes.

**Marco:** M0

**Dependências:** T-F01-02, T-F01-03

**Requisitos:** REQ-F01-04

**Cenários relacionados:** AC-F01-01

**Entregável e limite:** Scripts locais por OS com restore/build/test/export e diretório de artefatos.

**Critério de aceite:** Repetição com mesmo lock gera executáveis funcionais sem depender de servidor em runtime.

**Evidência:** Logs e smoke test dos dois executáveis.
