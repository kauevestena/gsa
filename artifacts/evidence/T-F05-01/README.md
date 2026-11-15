# Evidência — T-F05-01

Estado: em revisão. 37 testes GNSS/RNG passaram na [execução integrada M1](../T-F02-01/README.md). GNSS aplica erro comum compartilhado, viés horizontal persistente por célula e ruído independente com sigma do perfil×DOP. Abertura abaixo de 0,1 ou sinal ausente produz observação indisponível com posição/sigma ausentes.

Heading usa 3°/2°/1° de sigma, sem inferir rumo de velocidade parada. Informação não confiável permanece ausente. A saída contém medição e metadados de incerteza independente, nunca verdade ou erros realizados. Não há base, DGNSS/RTK, estimador nem integração de plataforma neste incremento.

SplitMix64+Box–Muller possui estado explícito, incluindo spare. Erro comum usa processo correlacionado com RNG separado, consulta repetida por tempo sem consumo, rejeição de tempo regressivo e replay JSON. Multipath usa FNV-1a UTF-8 estável e amplitude/fator explícitos. Fixtures comuns usam sigma 0,5 m/tempo de correlação 30 s; não mudam as sigmas nominais dos perfis.

Os testes usam 10.000 amostras por distribuição e 100 seeds fixas na comparação de ambiente, com médias em três erros-padrão e variância em ±20%. Estado/indisponibilidade reproduzem exatamente a sequência no mesmo runtime. O caso de ângulo `1e20` rad tem regressão para preservar ruído antes do wrap final. Implementação/algoritmos documentados em `src/Gsa.Core/Sensors/Gnss/README.md`; logs/TRX em `../T-F02-01/linux/`.
