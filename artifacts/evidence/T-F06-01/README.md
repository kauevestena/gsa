# Evidência — T-F06-01

Estado: em revisão. REQ-F06-01; cenário relacionado AC-F06-01. O incremento entrega schema/validação, não o CQ nem a interface do quadro de contratos de M2.

Entradas: `content/schemas/contract-v1.schema.json`, os quatro fixtures em `content/contracts/` e o exemplo original `specs/examples/contract-fachadas.json`, preservado. Unidades, métricas e limites são descritos em `content/schemas/README.md`; catálogos externos em `content/README.md`.

85 testes de Missions passaram na [execução integrada](../T-F01-02/README.md). Cobrem campos/enumerações, produtos incompatíveis, unidades ausentes, excelência incoerente, números não finitos, prazo, predicados, pesos/duplicações em parcial e conversão exata de inteiros decimais/exponenciais. O schema estrutural não expressa comparação entre campos ou consulta a catálogos; a semântica é validada pela CLI C#.

Reprodução: `dotnet test tests/Gsa.Missions.Tests --no-restore` com a toolchain configurada, ou `python3 scripts/verify.py --exports none` a partir da raiz. Logs/TRX completos estão em `../T-F01-02/linux/`. Sem seed: fixtures determinísticos. Aceite humano pendente; AC-F06-01 só será integrado com o CQ em M2.
