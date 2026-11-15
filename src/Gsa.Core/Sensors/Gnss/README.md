# GNSS e heading didáticos — T-F05-01

Este incremento sintetiza observações puras, sem Godot, estimador, base, DGNSS/RTK ou UI. A verdade é entrada apenas dos simuladores; os registros de saída contêm medição, disponibilidade, tempo, sequência e perfil, sem verdade nem componentes realizados do erro.

## Contratos e erros

`GnssSimulator.Observe(time, truePosition, profile, environment, sharedCommonError)` aplica `z=truePosition+sharedCommonError+environment.MultipathBiasEnu+independentNoise`. `GnssProfile.Cellphone` usa sigma-base de 3 m; `Navigation`, 1,5 m. `dop=1+4*(1-openness)` e `IndependentSigmaMetres=baseSigma*dop`. Esse campo descreve somente o ruído independente: não é uma promessa de incerteza total, pois erro comum e multipath não são removidos. Abertura abaixo de 0,1 ou disponibilidade falsa produz `Unavailable` e posição/sigma ausentes, nunca uma observação zero válida.

`CellMultipathBias.ForCell(seed, cellId, amplitudeMetres, facadeFactor)` produz viés horizontal persistente: uma direção uniforme derivada de FNV-1a 64 sobre o ID em UTF-8, XOR com a seed, seguida de SplitMix64. A amplitude declarada é multiplicada por `facadeFactor >= 1`; a direção não muda a cada amostra nem quando o fator muda. Não existe uma amplitude escondida escolhida pelo simulador.

`CommonGnssErrorProcess(seed, sigmaMetres, correlationTimeSeconds).At(time)` possui RNG separado e fornece o mesmo erro ENU para todos os sensores/base que consultem o mesmo tempo. O primeiro valor é uma normal estacionária de sigma configurável. Para `dt>0`: `alpha=exp(-dt/correlationTimeSeconds)` e `errorNext=alpha*error+sigma*sqrt(1-alpha²)*normal`, por eixo. Consultar novamente o mesmo timestamp não consome RNG; tempo regressivo é rejeitado. Sigma e tempo de correlação são explícitos no construtor. Fixtures usam 0,5 m e 30 s; esses números não alteram o baseline de perfil nem representam equipamento comercial.

`HeadingSimulator.Observe(time, trueHeading, profile, reliable)` usa um RNG independente e ruído normal angular. Perfis dos níveis 0/1/2 têm 3°/2°/1° de sigma, convertidos para radianos. A saída é normalizada em `[-pi,pi)`. Sem heading confiável, o ângulo e sigma são ausentes. Não há entrada de velocidade nem inferência de rumo a partir de deslocamento parado; não há um caminho que complete atitude com verdade oculta.

## Reprodução e estado

`GaussianRandom` usa SplitMix64 (incremento `0x9E3779B97F4A7C15`, misturadores `0xBF58476D1CE4E5B9` e `0x94D049BB133111EB`). Uniformes abertos `(0,1)` usam 52 bits e o centro do bin: `((u64>>12)+0.5)*2^-52`. Box–Muller usa `sqrt(-2*log(u1))` e seno/cosseno de `2*pi*u2`; o segundo normal fica no estado como spare.

`GaussianRandomState` guarda estado ulong, indicador e valor spare; snapshots dos simuladores também guardam a próxima sequência. O processo comum guarda ainda configuração, último tempo, componentes escalares do último erro e indicador de inicialização. Seu `ExportState()` é restaurado pelo construtor de estado; os testes incluem serialização JSON desses snapshots e comparação exata da série subsequente. Esses estados pertencem à geração, não às observações medidas nem a um estimador. Nenhum RNG usa `System.Random`, hash de string do runtime ou estado global. Cada componente usa sua própria instância/seed. Uma tentativa indisponível recebe sequência, mas não consome ruído independente/heading nem fabrica medição; replay preserva essa sequência de disponibilidade.

Fixtures estatísticos fixam seeds no código antes da execução. Usam 10.000 amostras, média dentro de três erros-padrão e variância dentro de ±20%; erro comum e multipath são zerados para isolar a sigma conhecida. A comparação céu/cânion reúne 100 seeds. Persistir/recarregar o estado preserva exatamente a série na mesma versão/build/runtime; comparação numérica entre plataformas usa tolerâncias, sem prometer bit a bit entre runtimes.
