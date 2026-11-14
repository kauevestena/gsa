# Evidência — T-F02-02

Estado: em revisão. Pré-cálculo `fibonacci_hemisphere_aabb_v1`: 32 direções uniformes em ângulo sólido, grade de 10 m, olho 2 m acima da origem. Caixas estáticas do manifesto são a mesma geometria da cena; o cache inclui versão, datum, origem, limites e hash canônico.

22 testes de céu passaram: aberto=1, interior coberto=0, fachada intermediária, contato de distância zero, grid recortado e limites, JSON, altura incompatível e invalidação por geometria/versão. Seis testes novos da CLI verificam criação atômica, preservação da saída em caso de erro e diagnósticos. Não são seguidas referências a arquivos não declarados no manifesto.

Na [execução integrada](../T-F02-01/README.md), CLI gerou 144 células e a engine/importador confirmou hash e consulta, tanto no projeto quanto no export Linux offline. Cache gerado fica fora do Git; o pipeline o recria antes do export. Logs `../T-F02-01/linux/precompute-sky.log`, `world-smoke.log` e `export-world-smoke-linux.log`.

Esse mapa é estático e de altura de referência; obstruções móveis, outros pisos e streaming dependem dos adaptadores posteriores. Consulta ausente retorna false, sem fabricar abertura. Sem seed: direções fixas documentadas em `src/Gsa.Tools/Sky/README.md`.
