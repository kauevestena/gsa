# Saves de carreira: versão 2 e recuperação

F5 grava a carreira e F9 recarrega. Aceitação e liquidação de contratos também gravam o estado. A cena agora conserva contrato e prazos, carteira/dívida, frota/habilidades, posição do jogador e do veículo estacionado, autonomia/armazenamento, calendário/clima, fotos brutas e miniaturas, próximo identificador de câmera, cobertura provisória, Geopédia, colecionáveis, objetivos/cutscenes e configurações de processamento.

Ao recarregar, o jogador e o veículo retomam parados. Os colecionáveis são reconstruídos para o dia restaurado. O modo livre usa um diretório separado e seus comandos F5/F9 não acessam a carreira.

## Compatibilidade

| Entrada | Comportamento |
| --- | --- |
| Save v1 válido | Confere o checksum e converte uma cópia em memória. O arquivo original não é alterado ao carregar. |
| Próxima gravação após v1 | Grava v2; mantém os bytes anteriores em `previous.save.json`. |
| Save v2 válido | Restaura os dados e preserva referências de cache compatíveis. |
| Catálogo anterior/aliases | Converte IDs documentados e invalida referências de caches derivados. |
| Versão futura ou conteúdo/modelo/mundo incompatível | Mostra diagnóstico e bloqueia sobrescrita manual/automática. |
| Arquivo corrompido, incompleto ou checksum divergente | Preserva o arquivo, bloqueia sobrescrita e oferece a geração anterior quando válida. |

O formato histórico PascalCase do envelope/estado foi mantido. O catálogo 6 acrescenta observações de distância e paradas nos portais de HIST-06 aos retornos LiDAR do catálogo 5. O catálogo 7 acrescenta a rede de distâncias do viaduto e o rascunho das alturas de HIST-07. O catálogo 8 acrescenta as distâncias da Orla e a seleção da receita de filtragem de HIST-08. O catálogo 9 acrescenta as leituras de estação e o estado de perseguição de HIST-09. Modelo e mundo continuam 1. Catálogos 1–8 são migrados em memória preservando fotos e lotes. Builds anteriores rejeitam catálogo 9 para preservar o novo estado. Aliases de IDs atuam no equipamento e no ID ativo da frota: `cart`/nomes de enumeração antigos apontam aos IDs de plataforma atuais; `cam_basic`, `cam_initial` e `camera_initial` apontam a `camera-initial`; `gnss_basic` aponta a `gnss-basic`. IDs dentro de observações brutas e definições já aceitas não são reescritos.

Versões de catálogo/modelo/mundo são explícitas. Invalidar caches remove referências derivadas do estado convertido; não apaga arquivos de lotes ou suas observações, não executa processamento e não cria transações, pagamentos ou proteção de dados pendentes.

## Recuperação

Na aba **Ajustes** do tablet, escolha **Recuperar geração anterior e preservar original**. O comando só está disponível quando há backup legível. Antes de substituir o save atual, a recuperação grava seus bytes em `preserved-<id>.save.json` no mesmo diretório; mantém `previous.save.json` intacto. Depois disso é possível continuar e salvar normalmente. A API de consulta `RecoverFromPreviousGeneration` continua somente leitura; a operação explícita é `RestorePreviousGeneration`.

Se não houver uma geração anterior válida, o jogo mantém o diagnóstico e os arquivos. Uma versão futura deve ser aberta pela versão do jogo que a produziu; recuperá-la como arquivo desta versão não tenta interpretar seus dados.

## Verificação e limites

Os testes de domínio constroem fixtures v1/v2/futuro/corrompido, incluindo dívida, contrato ativo e lotes pendentes/protegidos. O pipeline também executa `--city-save-smoke` na cena real e no executável exportado: captura foto na física, salva/recarrega frota/estacionamento e progresso, verifica a recuperação pelo botão do tablet e o isolamento do modo livre. A rotina usa um slot temporário próprio.

Saves v1 da cidade gravavam apenas parte do estado. Posição, fotos e colecionáveis ausentes nesses arquivos usam os valores iniciais; não são inventados. Em HIST-06, o save preserva retornos a bordo e protegidos com pose estimada, tempo físico, montagem e indicador de calibração. O scanner retoma pausado. Ao reativá-lo, as estações instrumentadas permitem retomar a coleta dentro da galeria, mesmo durante o bloqueio GNSS. A nuvem exibida é descartada ao recarregar e fica fora do lote científico. Tráfego, áudio, eventos transitórios e velocidade física não constituem replay de sessão; replay científico tem seu formato próprio.

A [integração dos perigos](city-hazards.md) conserva danos por veículo, lente/pneu/rack, marcos deslocados, timers físicos, ataques resolvidos, seguro e proteções por zona. Lotes protegidos conservam payload/checksum; IDs e sequência de câmera são validados também contra fotos enviadas. Salvar/carregar não reinicia contagem de bomba nem recria fotos perdidas. O perseguidor é reconstruído com o tempo restante, sem replay de trajetória.

Os [encontros cotidianos](city-everyday-encounters.md) adicionam cão/posição/contato, cambaleio, oferta/recusa, dano cosmético e estado completo de Gimbalina. A migração de catálogo 3 mantém seus perigos físicos e fotos; novos campos ausentes usam valores iniciais.
