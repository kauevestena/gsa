# Evidência — T-F01-03

Estado: em revisão. REQ-F01-03; cenário relacionado AC-F01-02. Entradas e formatos: `content/README.md`, quatro produtos, perfis camera/GNSS/LiDAR, proxies do Centro Velho e duas missões de história de fixture. São dados neutros, sem criar mundo ou campanha.

25 testes de Tools e 85 de Missions passaram na [execução integrada](../T-F01-02/README.md). Casos negativos incluem IDs ausentes/duplicados, NaN/overflow, geometria degenerada/autointersectante, região/área incorreta, buffer ou slots insuficientes, capacidade incompatível com sensor, missão com ciclo e parcial em história. A capacidade validada é a coerência de buffer/montagem declarada; viabilidade física de rota/contrato depende de marcos posteriores.

A CLI real foi executada com conteúdo válido (exit 0) e cópia cujo primeiro ID de fachada foi trocado por `missing_feature_for_smoke_test` (exit 2, diagnóstico no arquivo/campo). O pipeline cria/remove essa cópia em `work/`; o comando e saída estão em `../T-F01-02/linux/invalid-content.log`.

Reprodução: `python3 scripts/verify.py --exports none`. Sem seed; dados determinísticos. Não há comandos futuros retornando sucesso fictício. Auditoria geral de tradução, aquisição e CQ não são declarados completos. Aceite humano pendente.
