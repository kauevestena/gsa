# Catálogos neutros de conteúdo — M0

Este diretório contém fixtures técnicos dos quatro produtos, sem cenas, campanha ou equipamentos já desbloqueados. IDs e dados são estáveis; JSON UTF-8 usa `schema_version: 1`, `snake_case`, ponto decimal e números finitos. O validador não abre Godot.

`gsa-tools validate-content <diretório>` lê `catalog.json` e os JSON nas pastas `contracts/`, `profiles/`, `regions/` e `missions/`. Cada pasta tem ao menos um documento. Não segue links simbólicos. JSON malformado, propriedades repetidas, versões futuras e referências quebradas são rejeitados. Saída: `{"valid":true|false,"diagnostics":[{"path":"...","field":"$.campo","reason":"..."}]}`. Exit 0 para válido, 2 para conteúdo inválido e 1 para uso incorreto da CLI. Comandos de ferramentas posteriores ainda não são anunciados como implementados.

## Catálogo

`catalog.json` declara `clients` e `capabilities` como listas de IDs únicos e `mounting_slots` como lista de `{id, capacity}`. A capacidade é o número inteiro positivo de posições disponíveis no slot; não representa massa, armazenamento nem precisão. IDs de entidades são únicos dentro de cada tipo em todo o diretório, inclusive áreas, superfícies e feições entre regiões.

## Perfis

Cada perfil declara `id`, `type` (`camera`, `gnss`, `lidar`, `imu`, `dmi`), `grade` inteiro 0–6, `rate_hz`, `range_m`, `resolution` (objeto de dimensões/resoluções estritamente positivas), `noise` (objeto de desvios não negativos), `power_w`, `storage_bytes_per_sample`, `buffer_capacity_bytes`, `capabilities` e `mounting_requirements: {slot_id, count}`. Taxa, alcance, custo por amostra e buffer são positivos; consumo pode ser zero. O buffer precisa comportar pelo menos uma amostra completa. A contagem de posições não pode exceder a capacidade do slot. Capabilities e slots devem existir no catálogo; uma capability exigida por contrato precisa ser fornecida por algum perfil válido.

Esses campos concretizam o contrato lógico SensorProfile de `specs/interfaces.md`. As capacidades conhecidas `camera`, `gnss_basic` e `lidar` só podem ser concedidas, respectivamente, pelos tipos `camera`, `gnss` e `lidar`; declarar uma string não transforma um sensor em outro tipo. `buffer_capacity_bytes` é capacidade interna de uma fila de perfil, não o espaço real em disco nem o armazenamento do veículo. O teste dessa coerência não promete viabilidade de uma missão inteira; essa validação exige as rotas e medições de marcos posteriores.

## Regiões

Regiões declaram `id`, `bounds_enu: {min:[E,N,U], max:[E,N,U]}`, `areas`, `surfaces` e `features`. Os limites são finitos e estritamente crescentes em cada eixo. Áreas têm `{id, polygon_enu:[[E,N],...]}`: pelo menos três vértices distintos; último vértice igual ao primeiro é opcional; área absoluta maior que 1e-10 m²; sem cruzamentos ou sobreposição de arestas não adjacentes. O cálculo usa diferenças locais para preservar precisão em coordenadas grandes. Todas as posições devem caber nos limites da região.

Superfícies têm `{id, area_id}`; feições têm `{id, area_id, surface_id, class, material, position_enu:[E,N,U], tags:[], dynamic:false}`. As referências devem resolver na mesma região; superfícies e feições de contratos devem pertencer à região/área indicada pelo contrato. Essas definições são proxies de validação, sem construir o mundo 3D.

## Missões

Missões declaram `id`, `act` inteiro 1–3, `objectives:[{id, contract_id}]`, `events:[]`, `preconditions:[]`, `calm` booleano, `calm_variant` (ID ou null), `dialogue_keys:[]` e `unlocks:[]`. Neste catálogo M0, MissionDefinition representa missões de história dos três atos; templates procedurais são contratos independentes e não entram nesses objetivos. Objetivos têm IDs locais únicos e apontam para contratos; pré-condições e desbloqueios apontam para missões existentes. Pré-condições não podem formar ciclos. Missão não calma exige uma variante calma existente, distinta e marcada `calm:true`. Contratos vinculados a objetivos de missão precisam de `partial_policy.mode: disabled`, conforme `specs/modelos.md`. O validador cruza também `story_prerequisites` e `reward.story_unlocks` dos contratos.

Os contratos são validados por Gsa.Missions conforme o [schema e as métricas](schemas/README.md). São invariantes adicionais do catálogo: cliente, região, área, feições, superfícies, capabilities e missões precisam existir. O estado científico, relógios e pagamentos não são executados nesta etapa.
