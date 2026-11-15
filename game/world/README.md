# Manifesto neutro do quarteirão — T-F02-01

`tutorial-block.json` é a fonte única do graybox e de seus colliders. A cena Godot lê o mesmo documento distribuído no pacote; ferramentas posteriores podem lê-lo sem abrir a engine. Não existe uma segunda cópia geométrica em `content/`.

O formato inicial usa JSON UTF-8, `schema_version: 1`, IDs estáveis e números finitos. Versões são inteiros matemáticos positivos, inclusive quando escritos com decimal/expoente integral. A origem e o datum são os definidos em `specs/interfaces.md`; coordenadas e dimensões ficam em double até o adaptador Godot remover a origem.

## Campos

- Cabeçalho: `scene_id`, `map_version`, `datum_id`, `origin_enu:[E,N,U]` e `bounds_enu:{min:[E,N,U],max:[E,N,U]}`. O datum inicial é `SAO_DATUM_LOCAL_V1` e a origem `[500000,7400000,100]` m.
- `cells`: IDs, limites ENU e `environment_class`. São partições estáticas de referência; não implementam streaming nem declaram visibilidade de céu já calculada.
- `objects`: `id`, `class`, `material`, `region_id`, `surface_ids`, `tags`, `dynamic`, `position_enu:[E,N,U]`, `size_enu:[E,N,U]` e `collision`. A geometria inicial é uma caixa axis-aligned centrada em `position_enu`; todas as dimensões são estritamente positivas.
- Materiais iniciais: `asphalt`, `concrete`, `glass`, `water` e `metal`. As cores são apenas apresentação do graybox. Probabilidades de retorno e comportamento científico pertencem às tarefas de sensores.

Todo objeto possui classe/material, ao menos uma superfície e ao menos uma tag. IDs de objetos e células são únicos em suas categorias. As caixas devem caber nos limites da cena; as células têm limites positivos contidos na cena. `dynamic` é metadado de estado; esta bancada só constrói objetos estáticos. `collision:false` permite um proxy sem sólido, como a amostra de água, sem apagar sua semântica.

`facade_01` a `facade_20` preservam exatamente os centros ENU do catálogo M0 em `content/regions/old-center.json`. Cada face tem 8 m de largura, 0,2 m de espessura norte-sul e 2 m de altura, com pé em h=100 m. Há volumes de edifício atrás delas, entrada do escritório, ponto de descarregamento, dois controles e amostras de vidro/água. Os controles são distintos de checkpoints de qualidade; nenhum checkpoint ou posição verdadeira é usado para estimar observações neste incremento.

## Semântica e hash

`WorldSceneManifest` valida o documento sem Godot e expõe coleções somente leitura. O hash SHA256 canônico inclui versões, datum/origem/limites, células e todos os campos dos objetos em ordem estável por ID. Espaços ou ordem das propriedades JSON não mudam o hash; alterar pose, dimensão, material ou tags invalida-o. O cache de céu separado usa esse identificador para rejeitar uma revisão geométrica incompatível.

`TutorialBlockScene` cria renderização e colisão a partir dessas mesmas definições. Todo `StaticBody3D` recebe os metadados semânticos e um índice por instance ID; `SemanticRegistry.GetById` retorna um novo snapshot, sem expor coleção mutável compartilhada. Reconstruir o nó do quarteirão preserva IDs, transformações e hash.

Quando `tutorial-sky.json` está presente, `SkyCacheAdapter` valida seu hash/versão antes de habilitar consultas. Ausência do arquivo deixa `HasSkyCache` falso; não produz céu aberto fictício. O pipeline prepara o arquivo com `precompute-sky` antes de importar/exportar a cena; esse cache gerado não é uma segunda fonte geométrica.

## Cena e verificação

`TutorialBlock.tscn` usa câmera fixa de inspeção, luz e graybox; Escape retorna à tela inicial. Não há locomoção, captura, loja, contrato ou descarregamento funcional nesta tarefa.

Com `--smoke-test` ou `--bench-smoke`, a cena espera frames de física antes de consultar o espaço. Verifica as vinte fachadas, tags/material/superfícies, conversão ENU/alturas, raycast real que resolve a semântica de uma fachada e estabilidade após reconstruir a cena. O smoke exige o cache preparado e compara sua primeira célula com uma consulta pelo adaptador, emitindo `GSA_SKY_SMOKE_OK`. Em sucesso emite também `GSA_WORLD_SMOKE_OK` e encerra com exit 0; falha emite diagnóstico e exit 1. Testes puramente numéricos do manifesto ficam em `Gsa.Core.Tests`; uma execução headless não constitui inspeção gráfica nem benchmark de desempenho.
