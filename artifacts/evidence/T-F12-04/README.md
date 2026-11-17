# T-F12-04 — Migração e compatibilidade de saves

Aceita por revisão técnica delegada após CI Linux/Windows do PR #23 (17/11/2026), com pré-requisito T-F12-03 aceito. O incremento prioriza a integridade da carreira antes de ampliar os dados persistentes da UI avançada.

- Conversão v1→v2 em memória após verificação de envelope/payload e SHA-256.
- Aliases limitados a equipamento/frota, versões de catálogo/modelo/mundo e invalidação de referências a caches; observações, lotes, prazos, dívidas e transações são preservados.
- Gravação manual/automática bloqueada sobre saves inválidos ou futuros; recuperação explícita arquiva o original e mantém o backup.
- Cena de cidade conserva contrato ativo, frota, locomoção/estacionamento/recursos, câmera e sequência de IDs, clima, Geopédia, colecionáveis e estado narrativo. Sandbox usa outro diretório e não acessa a carreira.
- Regressões de domínio e smoke real de cidade/executável exigidos pelo pipeline. O smoke usa slot temporário, captura na física e testa o botão de recuperação.

Ver [formato, recuperação e limites](../../../docs/save-compatibility.md). A nuvem visual e outros estados transitórios não são promovidos a observações; dados não gravados pelos saves antigos não são inventados. Revisão gráfica Windows e gates humanos permanecem separados dos testes headless.

O [resumo CI](ci-summary.json) registra o código aprovado `e0b68b21b3f9d27c997ca0d4f7ba451dcfd1b776`, execução [44013474205](https://github.com/kauevestena/gsa/actions/runs/44013474205), com ambos os jobs nativos aprovados. O [relatório local](local-verification.json) contém 455 testes e 24 verificações aprovadas, com locks inalterados. Os logs de smoke da cena e do executável Linux estão nesta pasta; a CI preserva os exports nativos e os logs das duas plataformas. A atualização documental de aceite também deve passar na CI antes do merge.
