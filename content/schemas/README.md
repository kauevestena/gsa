# Contratos de conteúdo — M0

`contract-v1.schema.json` descreve `ContractDefinition` no formato JSON Schema draft 2020-12. O identificador `gsa.invalid` é um nome lógico, não um serviço consultado em runtime. O exemplo normativo em `specs/examples/contract-fachadas.json` é aceito sem alterações.

`Gsa.Missions.Content.ContractValidator.Validate(JsonElement, path)` valida estrutura e relações entre campos sem abrir Godot. Retorna diagnósticos imutáveis com `path`, `field` em JSONPath e `reason`. Versões futuras, campos duplicados/desconhecidos, números não finitos, enumerações desconhecidas e IDs vazios/duplicados são rejeitados. A ferramenta de conteúdo resolve referências externas de cliente, mundo, capacidades e missões numa segunda etapa. A auditoria geral das chaves de localização pertence aos incrementos de interface/localização; não é executada pelo validador de conteúdo deste incremento.

## Métricas quantitativas

| ID | Unidade e direção | Produtos |
| --- | --- | --- |
| `area_coverage` | `ratio`, `min` | Todos |
| `feature_completeness` | `ratio`, `min` | `feature_inventory` |
| `valid_image_fraction` | `ratio`, `min` | `images`, `feature_inventory`, `pavement_profile` |
| `absolute_rmse` | `m`, `max` | Todos, quando houver referência de checagem |
| `relative_rmse` | `m`, `max` | `feature_inventory`, `point_cloud`, `pavement_profile` |
| `density_coverage` | `ratio`, `min` | `point_cloud` |

Cada requisito quantitativo usa `kind: "threshold"`, `metric`, `unit`, `direction`, `limit` e `excellent`. Razões pertencem a `[0,1]`; distâncias são não negativas. Excelência é estritamente maior que aprovação em métricas mínimas e estritamente menor nas máximas. Completude de 100% sem margem usa o predicado `all_required_features`.

`area_coverage` e `density_coverage` exigem `surface_id` e `cell_size_m > 0`. `density_coverage` também exige `target_density_points_per_m2 > 0`: a métrica é a fração de células que atingem a densidade-alvo, não uma densidade global. Esses nomes concretizam os modelos descritos em `specs/modelos.md`, sem implementar o cálculo do CQ neste incremento.

## Predicados e compatibilidade

`all_required_features` aplica-se a `images` e `feature_inventory`. `privacy_redacted` e `format_valid` aplicam-se a todos. Cada predicado usa `kind: "predicate"`, `metric` e `expected: true`; não contém limiares quantitativos. Uma métrica só aparece uma vez em cada conjunto.

Privacidade e formato são invariantes globais mesmo se não aparecem explicitamente no template; omitir a linha não autoriza dispensá-los. `expected: false`, inclusive em entregáveis parciais, é inválido. O futuro fluxo de entrega deve sempre aplicar esses invariantes, conforme `specs/interfaces.md`.

`images` e `feature_inventory` exigem capacidade `camera`; `point_cloud` exige `lidar`; `pavement_profile` exige ambas, conforme o perfil combinado de F04. Capacidades extras são IDs resolvidos no catálogo; o validador de contrato não mantém uma enumeração fechada de sensores.

## Prazos e parcial

O prazo recomendado é positivo, e o final deve ser estritamente posterior. Taxa e teto de multa são frações de `[0,1]`; os fixtures conservam o baseline de 5% por hora e teto de 50%. Recompensas são inteiros não negativos representáveis em Int64. Notação decimal ou exponencial é aceita quando o valor é exatamente integral, como `400.0` ou `4e2`; o leitor compartilhado `JsonNumbers` verifica o lexema sem arredondamento, rejeitando frações mesmo quando pequenas demais para `decimal` ou `double`.

`partial_policy` é `{"mode":"disabled"}` ou `{"mode":"deliverables","deliverables":[...]}`. Entregáveis têm ID e peso positivos, área, feições e requisitos próprios. Os pesos somam 1 com tolerância `1e-9`; IDs/feições não podem ser cobrados duas vezes, e uma feição parcial deve pertencer ao conjunto global. O validador mantém conformidade global, mas não calcula pagamento.

O formato normativo do contrato não contém um discriminador de missão de história. Portanto a proibição de parcial em contratos de história deve ser aplicada ao resolver o vínculo de missão no catálogo, sem inferir semântica de prefixos de ID.

JSON Schema padrão não compara valores de campos irmãos nem consulta catálogos. A ordenação de prazo, a excelência estrita, a soma de pesos, a unicidade por métrica e a posse de feições são verificadas pelo validador C#. Publicar um JSON que passe apenas no schema estrutural não substitui `gsa-tools validate-content`.

## Fixtures

Os quatro documentos em `content/contracts/` exercitam `images`, `feature_inventory`, `point_cloud` e `pavement_profile`. Fachadas preserva o exemplo normativo; inventário inclui dois entregáveis independentes. Os demais usam a mesma geometria e os mesmos IDs de referência para testar schema/validação em M0; são fixtures de dados, sem missão jogável ou promessa de aquisição física já implementada.
