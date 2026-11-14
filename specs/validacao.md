# Estratégia de validação e evidências

## Níveis

| Nível | Execução / evidência |
| --- | --- |
| Documentação | `python3 specs/validate.py`: links, IDs, requisitos ligados a tarefas, aceites, marcos, DAG e JSON embutido |
| Core/Missions | xUnit via `dotnet test`; fixtures analíticos, estados e estatística reproduzível |
| Adaptador | Cenas headless para conversões, contratos e save; cena com GPU para câmeras, pontos e desempenho |
| Conteúdo | CLI valida JSON, referências, traduções, capacidades, rotas e variantes calmas |
| Jogabilidade | Roteiros manuais e playtests com gravação/observação consentida e relatório agregado |
| Distribuição | Smoke test de executáveis exportados em Windows/Linux limpos e offline |

Comandos do jogo serão concretizados em M0; não são apresentados como já disponíveis. O validador deste pacote é executável agora. Não substituir testes comportamentais por comparação textual de implementação.

## Fixtures numéricos obrigatórios

1. Transformação identidade, rotação de 90°, conversão ENU/Godot e round-trip; tolerância geométrica de [modelos](modelos.md).
2. Lever arm de 0,1 m, boresight de 0,001 rad a 100 m e offset de 0,02 s a 10 m/s, com resíduos previstos analiticamente.
3. Trajetória parada/reta/curva, interrupções GNSS de 5/30/60 s, MEMS/tática/navegação; medir deriva por 100 seeds, não exigir monotonicidade em sorteio individual.
4. Base válida versus ausente/distante; PPK com logs completos/incompletos; correções usam observações, nunca oracle.
5. Câmera com alvos visíveis/ocluídos, arrasto, rolling shutter, chuva/noite e triangulação degenerada.
6. Planos LiDAR conhecidos, oclusão, vidro/água, vegetação, chuva, duplicação de IDs, múltiplas passadas e LOD.
7. Calibração com perturbação conhecida, conjunto de ajuste e conjunto reservado; reprovação de configuração não observável.
8. CQ com célula parcialmente recortada, conjunto vazio, checkpoint ausente, erro acima/abaixo do limite e privacidade pendente.

Testes estatísticos aceitam média/variância dentro de intervalo de confiança registrado (baseline: três erros-padrão para média e ±20% para variância em 10000 amostras). Fixar seeds e não atualizar fixtures apenas para fazer um teste passar.

## Integração e regressão

- Contrato: aceitação → coleta → descarregamento → processamento → CQ → entrega, incluindo recoleta e repetição de comando de pagamento.
- Atraso no prazo recomendado versus expiração, inclusive dentro do escritório, ao avançar horário e após recarregar save.
- Destruição com zero, parte e todos os lotes protegidos; fuga da galinha; falta de bateria, espaço virtual e disco real.
- Processamento cancelado sem alterar brutos; mudança de calibração e invalidação de caches; trocar modo/idioma sem alterar resultado científico.
- Save interrompido antes/depois da troca de manifest, checksum inválido, versão anterior/futura e migração sem perda do original.
- Replay científico repete relatório no mesmo build; entre Windows/Linux, tolerância de 1e-6 m e 1e-8 relativo nos fixtures, sem exigir imagens idênticas.
- Procedural: 100 seeds por template; todas as instâncias possuem rota de referência factível, equipamento compatível e limite final suficiente.
- Campanha: todos os desbloqueios, três atos, último contrato, Cidade calma e ausência de bloqueio econômico.

## Benchmark de desempenho

Registrar build release, toolchain, modelo de CPU/GPU, RAM/canais, disco, OS, driver, resolução e configurações. Usar máquina de referência de [parâmetros](parametros.md); medir três execuções de cinco minutos após 30 s de aquecimento, seed e percurso fixos. Cena M1: quarteirão, 50 veículos, 30 pedestres, câmera ativa e bancada LiDAR; incluir seco/dia e chuva/noite. Cena M9: região mais onerosa, van, sensores, tráfego e visualização de nuvem ativos.

Em M1, dia/noite e chuva são presets estáticos de bancada de iluminação, partículas e snapshot de sensor. O ciclo dinâmico e a aderência ambiental completos entram em M3; sua ausência não impede o benchmark inicial. O quarteirão de M1 também contém as 20 fachadas e o descarregamento necessários ao tutorial de M2.

Aceite inicial: média ≥60 fps, frame time p95 ≤20 ms e p99 ≤33,3 ms; sem crescimento contínuo de memória na segunda metade; RAM ≤2 GiB, cache de nuvem ≤256 MiB; nenhuma perda silenciosa de amostra. Com VSync desligado para medição, registrar CPU/GPU separadamente. No jogo, permitir VSync. Métrica não atingida gera tarefa de correção e bloqueia o gate correspondente, não alteração automática da meta.

M1 também mede raios efetivos/s, taxa por perfil, tempo para montar um quarteirão e backlog; a implementação decide otimização conforme evidência. Raycast/GPU/LOD alternativos precisam manter fixtures e contagem real dos dados.

## Playtest de M2 e M3

M2: cinco pessoas sem conhecimento prévio de geomática, sessão de até 30 min. Pelo menos quatro devem concluir um contrato com no máximo duas intervenções; pelo menos quatro devem identificar uma causa de reprovação e a ação corretiva. Pelo menos três devem escolher realizar outro contrato após concluir o primeiro. Registrar também desconforto, controle e confusão, sem inferir validade estatística ampla dessa amostra.

M3: repetir com cinco jogadores e cinco estudantes; pelo menos quatro de cada grupo devem concluir e corrigir uma coleta, e três de cada grupo desejar continuar. Estudantes devem relacionar ao menos dois efeitos observados a iluminação, sobreposição, GNSS ou controle. Falha gera iteração no MVP antes de liberar M4, sem descartar relatos qualitativos quando os números passam.

## Revisão de conteúdo

Verificar nos dois idiomas: terminologia/glossário, humor adaptado, espaço de UI, números, unidades, plural, legendas e correspondência voz/texto. Toda fala obrigatória precisa de arquivo nos dois idiomas em M8; não aceitar silêncio de fallback como dublagem concluída. Revisão técnica confirma explicações da Geopédia e ausência de promessas científicas não simuladas.

Arte/roteiro: zero dano à galinha, zero arma utilizável pelo protagonista, facções fictícias, acidentes sem sangue, sinais visíveis dos perigos, variante calma de cada missão perigosa. Rádios, música, personagens e assets devem constar no manifesto de direitos de uso.

## Saída de um marco

Todas as tarefas daquele marco devem estar aceitas, seus pré-requisitos satisfeitos e o relatório do gate deve ligar evidências por tarefa/requisito. Pendências de publicação podem permanecer até M9; pendências essenciais ao teste do próprio marco não podem. Bugs que impedem campanha, perdem dados protegidos, duplicam pagamento ou violam isolamento da verdade são bloqueadores.
