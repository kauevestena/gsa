# Evidência — T-F02-01 e primeiros incrementos M1

Estado: em revisão técnica delegada, antes da CI do PR. Base: merge M0 `427fd32d624b7dcfe809ccffe168d368062cb28e`; branch `codex/m1-world`. Registra T-F02-01, T-F02-02, T-F04-01 e T-F05-01, sem declarar o gate M1 inteiro aceito.

## Reprodução

```sh
python3 scripts/verify.py --offline-smoke
```

Em Windows, `py -3 scripts/verify.py --exports host`. O pipeline gera o cache do céu antes de importar/exportar. Testa a tela inicial e o quarteirão separadamente no projeto e no export nativo. Registros completos em `linux/`, comandos/versões/resultado/hashes dos exports em `linux/report.json`.

## Resultado local

- Debian x86-64, SDK .NET 10.0.404, runtime 10.0.15, Godot 4.7.2 .NET; física 60 Hz, Compatibility.
- 274 testes .NET passaram, sem falhas/ignorados: Core 140, Missions 85, localização pura 18 e Tools 31. Os 10 testes Python do instalador passaram.
- Build sem warnings/erros; 17 locks inalterados. Nenhuma dependência nova.
- A fonte geométrica única `game/world/tutorial-block.json` gerou 48 objetos, 47 colliders e as vinte fachadas com IDs/posições do catálogo M0.
- Na janela segura de física, o smoke validou metadados, snapshots defensivos, alturas e ENU, raycast real de `facade_01` e reconstrução com os mesmos IDs/hash/transformações.
- Céu: 144 células, 32 direções por célula; o adaptador importou o cache e confirmou a consulta na mesma revisão. Hash `8bb5fafa4d764fba0d9281dbca0b7e7618ce08a96e8afe25d3cd68a6dfe812cc`.
- Exports Linux/Windows foram gerados. O export Linux carregou mundo/cache e repetiu os smokes com SDK removido do ambiente e namespace sem rede externa.

Os testes puros incluem 11 casos de alturas, 22 de manifesto, 22 de céu, 34 de câmera e 37 de GNSS/RNG, além dos testes anteriores. Fixtures geométricos são analíticos. Estatística usa seeds fixas em código, 10.000 amostras por distribuição e comparação de céu/cânion sobre 100 seeds; média em três erros-padrão e variância em ±20%. Mesmo estado/build/runtime reproduz a sequência, inclusive o spare do Box–Muller e indisponibilidade.

## Revisão e limites

A revisão independente não encontrou bloqueio no uso previsto. Um caso de heading finito muito grande (`1e20` rad) perdia ruído por arredondamento; a entrada agora é normalizada antes da soma, com teste de regressão. A CLI de céu também preserva diagnóstico correto de caminho de saída inválido.

O quarteirão é estático e usa câmera fixa de inspeção; não há locomoção, streaming, captura jogável, processamento ou campanha neste incremento. Câmera/GNSS são modelos puros; observações não transportam verdade ou erros realizados para um estimador. Inspeção gráfica, isolamento de rede Windows e benchmark na máquina de referência continuam separados. A CI nativa do PR deve passar antes do merge, conforme autorização do usuário.
