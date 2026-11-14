# GSA

Jogo offline de levantamento móvel em São Datum, em implementação guiada pela [pasta specs](specs/README.md).

A fundação **M0** contém Core C# independente, validadores de contratos/catálogos, ferramentas de conteúdo, fachada Godot e localização PT-BR/EN-US. A cena mínima permite trocar o idioma e demonstra números e plural; mundo, aquisição científica, CQ e campanha pertencem aos marcos seguintes.

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

Consulte os [comandos manuais e versões](docs/toolchain.md) para abrir o editor. Distribua a pasta inteira de cada export, incluindo PCK e runtime .NET. Ferramentas, caches, executáveis e saves ficam fora do Git. A instalação e o restore usam rede; o jogo exportado usa runtime autocontido.

## Estado

Os incrementos M0 estão em revisão: **142 testes .NET e 10 testes Python passaram**, build sem warnings/erros, CLI com exit 0/2 comprovado, exports Linux/Windows gerados e export Linux headless offline verificado. Ver [evidências da continuação](artifacts/evidence/T-F01-02/README.md), [toolchain](docs/toolchain.md) e [dependências](docs/dependencies.md).

Execução nativa Windows, verificação gráfica e revisão humana permanecem pendentes. Nenhuma caixa de aceite foi marcada; o gate M0 não é declarado aceito. O usuário autorizou continuar o desenvolvimento em M0 durante essas pendências, conforme `specs/README.md`. O próximo marco do roadmap é M1, a bancada de viabilidade.

O usuário autorizou publicar branches/PRs no repositório existente e mesclar após a CI passar. O desenvolvimento prossegue por incrementos; a licença do jogo continua pendente conforme EXT-02.
