# Decisões, conflitos resolvidos e pendências

## ADRs aceitas como baseline

| ID | Decisão e justificativa | Consequência |
| --- | --- | --- |
| ADR-001 | Godot 4 .NET + Core C# independente, conforme original e conversa | GDScript não implementa matemática científica duplicada; engine/SDK exatos são fixados em M0 após verificação oficial |
| ADR-002 | Física didática, causal e verificável, sem processar sinais GNSS RF reais | Fórmulas e limitações explícitas; nenhuma melhoria artificial do CQ |
| ADR-003 | Especificar jogo completo; organizar em M0–M9 e expansões | Conteúdo avançado tem tarefas próprias, sem crescer o MVP |
| ADR-004 | 4 km², MVP 0,25 km²; quatro regiões e sete níveis | Mundo modular e expansão preservando IDs |
| ADR-005 | Dublagem PT-BR/EN-US integral na 1.0 | Textos desde M0; produção de voz depende do roteiro estabilizado; MVP aceita voz provisória |
| ADR-006 | Gratuito e código aberto, sujeito à decisão institucional de licença | Preparar manifests/licenças; não publicar nem presumir autorização da UTFPR |
| ADR-007 | Meta de design 14 anos; Elias fixo; galinha intacta; jogador sem armas | Roteiro e arte coerentes; classificação oficial não é prometida |
| ADR-008 | Retrabalho + tolerância com expiração explícita; parcial só por contrato | Substitui contradições entre pagamento parcial, multa e falha total |
| ADR-009 | Descarregamento é transferência local dentro da ficção do jogo | Wi-Fi/escritório não dependem de rede real; saves preservam custódia dos dados |
| ADR-010 | Destruição/fuga encerra a saída, não apaga lotes protegidos nem necessariamente o contrato | Recoleta permitida até a expiração; elimina contradição de falha total com perda parcial |
| ADR-011 | LiDAR do protótipo é bancada, não equipamento do MVP | Desempenho investigado cedo; desbloqueio jogável começa no nível 4 |
| ADR-012 | Raycasts no contexto seguro da física; workers só sobre dados independentes | Corrige a promessa incondicional de raycasts fora da thread principal |
| ADR-013 | Replay registra observações/poses, além de entradas/seeds | Core reproduzível; não pressupõe física da engine determinística entre máquinas |
| ADR-014 | Cinco contratos do MVP significam cinco templates repetíveis | Três missões de história fixas são separadas da geração procedural |
| ADR-015 | Exportações e plataformas extras após 1.0 | CSV também faz parte de E1; ferramentas internas podem emitir CSV diagnóstico antes, sem UI de exportação ao jogador |
| ADR-016 | Um contrato ativo, 60 Hz físico, calendário ×60, dados e parâmetros versionados | Reduz concorrência inicial; avanço de calendário no escritório respeita expiração |
| ADR-017 | Fachadas/fotos iniciais podem omitir RMSE; inventários exigem observações trianguláveis/controles | Evita usar profundidade verdadeira de câmera para inventar posição estimada |
| ADR-018 | PO para traduções, CSV versionado para glossário, vozes por ID/locale | Português usa plural e texto contextual; humor adaptado por idioma |
| ADR-019 | Save versionado com geração anterior; compatibilidade com versão imediatamente anterior | Migração testável; versões futuras não são sobrescritas |
| ADR-020 | Renderer Compatibility e controllers próprios simples; extensões só por medição | Não assumir física de veículo pronta ou backend específico no original |

As ADRs de produto 001–008 e 015 incorporam escolhas expressas na conversa. Detalhes operacionais das demais e tabelas de balanceamento são baseline de implementação proposto por este pacote, revisável com evidência. Revisões mantêm o ID, registram data/motivo e atualizam tarefas afetadas.

## Lacunas externas, com responsável e momento

| ID | Informação/decisão | Responsável funcional | Quando bloqueia | Saída necessária |
| --- | --- | --- | --- | --- |
| EXT-01 | Pessoas e disponibilidade | Coordenação do projeto | Apenas compromisso de datas | Capacidade e calendário; marcos continuam executáveis sem datas |
| EXT-02 | Registro, titularidade e licença do código/assets | Coordenação com Agência de Inovação UTFPR | Publicação pública de M9 | Registro da decisão institucional, licença escolhida e direitos de distribuição |
| EXT-03 | Título público | Direção de produto | Identidade final e publicação M9 | Nome aprovado e substituição das referências externas ao codinome |
| EXT-04 | Canal de distribuição | Coordenação | Upload público M9 | Canal/conta/artefatos e autorização específica para publicação |
| EXT-05 | Classificação oficial | Produção | Distribuição onde aplicável | Documentação exigida pelo canal/território; meta interna continua 14 |
| EXT-06 | Elenco e gravações bilíngues | Produção de áudio | Aceite de M8/M9 | Arquivos, contratos/autorizações de uso, revisão linguística e manifesto completo |
| EXT-07 | Máquina de referência disponível | Responsável técnico | Aceite de desempenho M1/M9 | Modelo/driver/OS e relatório; testes em outra máquina são preliminares |

Nenhuma tarefa pode declarar resolvida uma pendência por silêncio. Dependência institucional não bloqueia Core, ferramentas, protótipo ou documentação; bloqueia somente o aceite/ação indicada.

## Fontes técnicas

- [Godot: C# e SDK](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html).
- [Godot: acesso seguro à física](https://docs.godotengine.org/en/stable/tutorials/physics/ray-casting.html).
- [Godot: localização e recursos por idioma](https://docs.godotengine.org/en/stable/tutorials/i18n/internationalizing_games.html).
- [GeoJSON RFC 7946](https://www.rfc-editor.org/rfc/rfc7946).

Consultadas em 13/11/2026. São referências de API/formato, não comprovação de versões futuras ou das afirmações legais/comerciais do documento original. Nenhuma conclusão sobre elegibilidade Unity/Unreal é necessária para implementar este plano.
