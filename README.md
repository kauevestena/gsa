# GSA

Jogo offline de levantamento móvel em São Datum, em implementação guiada pela [pasta specs](specs/README.md).

A fundação **M0** foi integrada no [PR #1](https://github.com/kauevestena/gsa/pull/1), após CI Linux/Windows. O início de **M1** acrescenta um quarteirão 3D semântico, vinte fachadas, modelo de alturas, visibilidade estática do céu e modelos puros de câmera/GNSS. A tela inicial permite trocar idioma e abrir o quarteirão; Escape retorna à tela inicial. Locomoção, aquisição integrada, CQ e campanha permanecem nos incrementos seguintes.

## Preparar e verificar

Requisitos: Git, Python 3.10+, Linux ou Windows x86-64 e as [dependências nativas](docs/toolchain.md). Execute na raiz:

```sh
python3 scripts/install_toolchain.py
python3 scripts/verify.py
```

Em Windows, substitua `python3` por `py -3`. O verificador configura os caminhos do SDK/editor, confere o grafo de dependências, executa testes Python/xUnit e a CLI com conteúdo válido/inválido, compila a solução, testa a fachada/localização e exporta ambos os sistemas. Usa locks versionados sem modificá-los. Logs e resultados ficam em `artifacts/logs/<host>/`; exports em `artifacts/exports/linux/` e `artifacts/exports/windows/`. Esses diretórios gerados são limpos a cada execução.

Em Linux com namespaces de usuário habilitados, `python3 scripts/verify.py --offline-smoke` testa também o export Linux sem rede. `--exports none` omite os exports; `--exports host` gera apenas o pacote nativo. `scripts/check_bootstrap.py` continua disponível como entrada compatível para o mesmo pipeline.

## Domínio e conteúdo sem Godot

Com o SDK local no PATH, os comandos abaixo não carregam a engine:

```sh
dotnet restore Gsa.Domain.slnf --locked-mode
dotnet test Gsa.Domain.slnf --no-restore
dotnet run --project src/Gsa.Tools -- validate-content content
```

A CLI retorna JSON com arquivo, JSONPath e motivo: exit 0 para conteúdo válido, 2 para inválido e 1 para uso incorreto. Os [catálogos neutros](content/README.md) incluem os quatro produtos; o [schema de contrato](content/schemas/README.md) preserva o exemplo normativo. São fixtures de desenvolvimento, sem missões jogáveis.

O pré-cálculo de céu usa a mesma [geometria neutra](game/world/README.md) que a cena e os colliders:

```sh
dotnet run --project src/Gsa.Tools -- precompute-sky game/world/tutorial-block.json game/world/tutorial-sky.json
```

O pipeline gera esse cache antes de importar/exportar a cena. O cache derivado fica fora do Git e só é aceito quando versão/hash correspondem ao manifesto. Há [documentação do modelo](src/Gsa.Tools/Sky/README.md), [câmera](src/Gsa.Core/Sensors/Camera/README.md) e [GNSS/heading](src/Gsa.Core/Sensors/Gnss/README.md).

Consulte os [comandos manuais e versões](docs/toolchain.md) para abrir o editor. Distribua a pasta inteira de cada export, incluindo PCK e runtime .NET. Ferramentas, caches, executáveis e saves ficam fora do Git. A instalação e o restore usam rede; o jogo exportado usa runtime autocontido.

## Estado

M0 tem aceite técnico delegado e CI nativa aprovada nos dois sistemas; ver [registro da CI](artifacts/evidence/M0-ci/summary.json) e [evidências históricas](artifacts/evidence/T-F01-02/README.md). Os incrementos iniciais M1 passam por testes puros, física headless da cena e verificação do export. [Toolchain](docs/toolchain.md) e [dependências](docs/dependencies.md) permanecem fixadas.

Inspeção gráfica, isolamento de rede Windows e benchmark na máquina de referência ainda não foram realizados. CI headless não os substitui. O usuário delegou revisão técnica e merges com CI verde; requisitos de playtest/produção humana permanecem explícitos. O gate M1 depende também de locomoção, sensores integrados, tráfego, LiDAR e medição de desempenho.

O usuário autorizou publicar branches/PRs no repositório existente e mesclar após a CI passar. O desenvolvimento prossegue por incrementos; a licença do jogo continua pendente conforme EXT-02.
