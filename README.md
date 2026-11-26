# GSA

Jogo offline de levantamento móvel em São Datum, em implementação guiada pela [pasta specs](specs/README.md).

O domínio de simulação chegou ao primeiro incremento de M7, com sensores, contratos, CQ, processamento, economia, persistência e montagem regional testados. Essa cobertura de bibliotecas não significa que todas as funcionalidades estejam integradas ao jogo.

## Treinamento jogável

A tela inicial permite trocar idioma e abrir o quarteirão. O exercício **Três fachadas** conecta fotos da câmera na cena, triangulação com paralaxe, descarga, processamento, CQ/recoleta, pagamento e save/load. Comece aceitando o serviço perto do escritório; use W/S/A/D para mover/frear/virar, C para fotografar, V para alternar a câmera e R para abrir o caderno com mapa, fotos, diagnóstico dos alvos, CQ e guia. Instruções e limites em [Treinamento jogável](docs/live-training.md).

O treino usa poses ideais e imagens sintéticas, está marcado como **em desenvolvimento** e é separado da campanha histórica. A cena de cidade oferece Carreira/Modo Livre, acesso à frota e contratos simplificados. A integração de CQ avançado e campanha continua em desenvolvimento; a cobertura das bibliotecas não equivale ao aceite dessas missões na cena.

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

A CLI retorna JSON com arquivo, JSONPath e motivo: exit 0 para conteúdo válido, 2 para inválido e 1 para uso incorreto. Os [catálogos neutros](content/README.md) incluem os quatro produtos; o [schema de contrato](content/schemas/README.md) preserva o exemplo normativo. São catálogos e fixtures de desenvolvimento; o exercício interativo atual é descrito acima.

O pré-cálculo de céu usa a mesma [geometria neutra](game/world/README.md) que a cena e os colliders:

```sh
dotnet run --project src/Gsa.Tools -- precompute-sky game/world/tutorial-block.json game/world/tutorial-sky.json
```

O pipeline gera esse cache antes de importar/exportar a cena. O cache derivado fica fora do Git e só é aceito quando versão/hash correspondem ao manifesto. Há [documentação do modelo](src/Gsa.Tools/Sky/README.md), [câmera](src/Gsa.Core/Sensors/Camera/README.md) e [GNSS/heading](src/Gsa.Core/Sensors/Gnss/README.md).

Consulte os [comandos manuais e versões](docs/toolchain.md) para abrir o editor. Distribua a pasta inteira de cada export, incluindo PCK e runtime .NET. Ferramentas, caches, executáveis e saves ficam fora do Git. A instalação e o restore usam rede; o jogo exportado usa runtime autocontido.

O [protocolo de desempenho do LiDAR](docs/lidar-performance.md) mede CPU, alocações e recargas de chunks, com hashes completos para comparar os resultados científicos entre revisões.

A carreira usa [save v2 com migração e recuperação](docs/save-compatibility.md): preserva fotos, contrato, frota, estacionamento e progresso; protege saves futuros/corrompidos contra sobrescrita e isola o modo livre.

O tablet da cidade inclui [Geopédia avançada e diagnóstico técnico](docs/advanced-survey-tablet.md): 30 verbetes bilíngues, limites do contrato ativo, parâmetros da prévia LiDAR e territórios de atenção. A [navegação ao vivo](docs/live-navigation.md) conecta observações GNSS/IMU/DMI, trajetória e incerteza ao mapa/HUD; as fotos de veículos e os retornos LiDAR de campo de HIST-06 guardam a pose estimada. HIST-06 usa o CQ de cobertura, densidade e placas independentes para fechar a entrega do túnel. A [primeira integração dos perigos](docs/city-hazards.md) conecta facções, lente, GNSS, marcos, pneus, bloqueios, perda de veículo e proteção dos dados enviados.

## Estado

Consulte o [roadmap](specs/roadmap.md) e as evidências por tarefa para o estado técnico das bibliotecas. A integração jogável atual está documentada separadamente; aceite técnico e testes automatizados não equivalem à aprovação humana de um marco.

Os gates humanos M2/M3 seguem em `pending_human_playtest_cohort`. O desempenho na GPU de referência continua em `pending_reference_hardware`. Inspeção gráfica e isolamento de rede Windows permanecem separados da CI headless. A licença do jogo ainda está pendente conforme EXT-02.

Os [encontros de cães e estacionamento](docs/city-everyday-encounters.md) ligam perseguição, petisco, cambaleio, pagamento/recusa e dano cosmético à cidade. Estresse e fase de Gimbalina são salvos e afetam a exposição das câmeras iniciais.

## Desafio da Gimbalina

Na tela inicial, escolha **Desafio da Gimbalina** para sair de bicicleta no Parque Central de São Datum. W ou ↑ pedala, A/D ou ←/→ viram e S ou ↓ freia. Cruze o arco de largada, passe entre as bandeiras dos três portões e cruze a chegada. O cronômetro mede os 200 m entre os arcos. A medalha exige que a cabeça de Gimbalina fique dentro de ±5° em pelo menos 95% das amostras. Frear antes de virar reduz a inclinação da bicicleta. R reinicia a tentativa rápida e Esc volta ao menu.

O recorde é local e separado por plataforma. A prova também pode ser encontrada na cidade, junto ao parque, de bicicleta ou carrinho e entre contratos. Uma vitória na cidade rende 100 Coords. A entrada rápida guarda apenas o recorde da prova.
