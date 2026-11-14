# Evidência — T-F01-04

Estado: em revisão com pendência de plataforma. REQ-F01-04; cenário relacionado AC-F01-01.

`python3 scripts/verify.py` em Linux ou `py -3 scripts/verify.py` em Windows configura toolchain/cache, verifica specs/grafo, executa testes e CLI, restaura/builda com locks, importa, testa a cena e exporta ambos os sistemas. `scripts/check_bootstrap.py` é uma entrada compatível. O runner usa 17 locks por projeto/configuração/RID e rejeita alteração deles durante o pipeline, além de erros Godot mesmo com exit zero.

Execução comprovada em Linux: 142 testes .NET e 10 Python; CLI válida/inválida; build sem warnings/erros; exports Linux/Windows; smoke do export Linux sem SDK configurado, em namespace sem rede externa, com fachada e PO carregados. Os diretórios gerados de logs/export são limpos antes de cada execução para evitar evidência de arquivos antigos.

Comando, versões, TRX, logs e hashes de todos os arquivos dos exports: [execução integrada](../T-F01-02/README.md) e `../T-F01-02/linux/report.json`. Binários não foram versionados nem enviados a serviço de publicação.

Windows nativo/offline e verificação gráfica continuam pendentes; export cruzado não os substitui. O jogo mínimo não acessa serviços em runtime. AC-F01-01 e gate M0 sem aceite humano. Sem seed: fixtures determinísticos.
