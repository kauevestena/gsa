# Geopédia avançada e diagnóstico da cidade

Na Carreira ou no Modo Livre, abra o tablet com **R**, **Tab**, **M** ou o botão da HUD. A aba **Geopédia** reúne 30 verbetes: 16 iniciais/colecionáveis e 14 avançados. Escolha um título para ler exemplo, unidades/efeito e limites do modelo. Um verbete bloqueado mostra seu marco de desbloqueio, sem antecipar o corpo do texto.

Os conceitos avançados desbloqueiam por conceito/marco ou ao usar a plataforma correspondente. Carro (nível 3) libera montagem, sincronização, IMU, base/RTK/PPK, RMSE, calibração, história e atenção; Kombi (nível 4) acrescenta DMI/ZUPT, densidade, oclusão e deriva; picape (nível 5) acrescenta múltiplos retornos. Isso não concede cartas, marcos visitados ou missões concluídas. IDs desbloqueados continuam no save v2 da cidade; nenhum dado científico é recalculado ao trocar idioma.

## Aba Técnico

O painel separa três informações:

- **Prévia LiDAR:** perfil, taxa nominal de raios primários, taxa programada após amostragem de canais, alcance e sigma de distância do perfil, quantidade de pontos no anel de exibição e capacidade do anel. Só aparece para a plataforma equipada com LiDAR. Uma prévia já coletada pode continuar visível após desembarcar.
- **Exigências do contrato ativo:** comparador mínimo/máximo, limite e valor excelente, unidade, predicados verdadeiros/falsos e contexto de superfície/célula/densidade-alvo quando definido no contrato. Inclui exigências de entregáveis parciais com seu ID e área. Os números usam a cultura selecionada e preservam o valor do limite; não arredondam um limite pequeno para zero.
- **Atenção territorial:** valores e níveis do rastreador, intervalo restante e estado de proteção tranquila. O número entre colchetes corresponde ao polígono no mapa. Pontuação e nível não significam que uma ação hostil esteja em andamento; a proteção continua explícita.

**Mostrar nuvem da prévia** altera apenas a visibilidade do MultiMesh. A tecla **L** continua controlando a captura. **Mostrar territórios no mapa** desenha contornos e rótulos numerados/níveis além da cor, sem alterar o rastreador. Controles de atenção ficam ocultos antes de descobrir o sistema; não aparecem falsos campos de sensor no carrinho inicial.

Os perfis usam a mesma seleção e passo de canais da aquisição da cena:

| Plataforma | Perfil | Raios primários nominais/s | Raios primários programados/s na prévia |
| --- | --- | ---: | ---: |
| Kombi | L4 | 1.000 | 1.000 |
| Picape | L5 | 8.000 | 4.000 |
| Van | L6 | 16.000 | 4.000 |

São taxas programadas, não medição de FPS, retornos úteis ou densidade. A prévia possui no máximo 18.000 pontos e ainda reduz os retornos enviados ao desenho. As amostras originais dos produtos do domínio e seus representantes de LOD permanecem conceitos distintos. Esconder a nuvem não reduz nem valida a coleta.

## Limites desta integração

A cidade usa poses ideais na prévia. Não há trajetória estimada ligada a esse painel: **deriva, sigma de trajetória e RMSE oficial são apresentados como indisponíveis**, sem valor zero ou faixa artificial. Sigma de distância do perfil é apenas um parâmetro do simulador. PPK pertence ao produto processado e não vira um estado GNSS ao vivo.

O escritório e os contratos da cidade ainda são simplificados. O painel mostra as exigências efetivamente presentes no contrato aceito; não inventa exigências do roteiro ou promove a prévia a aprovação oficial. O [treinamento separado](live-training.md) já usa o fluxo integrado de produto/CQ. Conectar a trajetória estimada e o CQ avançado da campanha continua pendente.

O catálogo está em `GeopediaCatalogCoordinator.AllEntries`; `MvpEntries` continua disponível para os fixtures do MVP. `AuditBilingualCatalog` agora verifica os dois conjuntos e todos os termos no CSV. Modelos e valores são revisados contra [modelos](../specs/modelos.md), [parâmetros](../specs/parametros.md) e os tipos reais de perfil/contrato/rastreador.

O diagnóstico só atualiza a 10 Hz enquanto o mapa ou a aba Técnico está visível. Não reconstrói contratos/frota nem traduz todos os corpos da Geopédia a cada tick. O leitor traduz somente o artigo selecionado. Esses limites de atualização não constituem benchmark de GPU.

## Verificação

```sh
python3 scripts/install_toolchain.py
python3 scripts/verify.py --exports host
```

`AdvancedSurveyPresentationTests` verifica cobertura bilíngue, persistência dos IDs, desbloqueio progressivo, taxas comparadas com a execução real das fatias de raios, limiares locais sem perda, contexto de densidade e snapshots de atenção. O smoke da cidade existente também exige `GSA_ADVANCED_SURVEY_SMOKE_OK`, tanto no editor quanto no executável nativo. Ele interage com os controles reais, executa aquisição com nuvem escondida, troca idioma sem mudar desbloqueios/dados, testa artigos bloqueados e restaura o fixture de carreira. Usa apenas o slot temporário do smoke de saves.

Essas verificações headless não comprovam aparência ou navegação por controle. Screenshots PT-BR/EN-US, inspeção a 150% e o overlay da trajetória estimada continuam pendentes para o aceite integral de **T-F11-05**.
