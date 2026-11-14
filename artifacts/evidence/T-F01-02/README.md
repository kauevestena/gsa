# Evidência — T-F01-02 e integração M0

Estado: em revisão, sem aceite humano ou gate M0 aceito. Implementação em 14/11/2026, branch local `codex/m0-foundation`, sobre a baseline `477ae796f3fe60c8df04ad7b2254bc99132695bb` e o incremento local T-F01-01. O usuário autorizou continuar o desenvolvimento durante as pendências, conforme `specs/README.md`; Windows e verificação gráfica não foram presumidos aprovados.

## Escopo

T-F01-02 entrega solução e referências unidirecionais, geometria double e fachada C#/GDScript. As tarefas M0 T-F06-01, T-F01-03, T-F11-01 e T-F01-04 foram integradas nesta execução; seus critérios e limites ficam nas pastas de evidência correspondentes. Não há aquisição, CQ, ledger, mundo ou missões jogáveis de marcos posteriores.

Core/Missions/Tools usam o SDK Microsoft .NET, sem referência a Godot. O filtro `Gsa.Domain.slnf` executa quatro suítes independentes da engine. Testes de política/formatação de idioma compilam os dois helpers puros por source link; a integração com PO/TranslationServer é verificada separadamente na engine.

## Reprodução e configuração

Na raiz, após instalar a toolchain:

```sh
python3 scripts/verify.py --offline-smoke
```

Em Windows x86-64, `py -3 scripts/verify.py` executa os mesmos passos e testa o export Windows nativamente. O isolamento de rede do script usa namespaces Linux e não está implementado no Windows; o teste offline nesse OS exige desabilitar a rede e registrar o procedimento.

Configuração desta execução: Debian 13 x86-64; Godot `4.7.2.stable.mono.official.ed1daf0bf`; SDK `10.0.404`; runtime `10.0.15`; renderer Compatibility e física 60 Hz. Fixtures analíticos, sem aleatoriedade/seed: nenhum sensor estocástico foi introduzido. Transformações usam tolerância `max(1e-6 m, 1e-8 relativo)` conforme modelos; o smoke do adaptador usa frações binárias exatas junto à origem grande.

## Resultado

| Verificação | Resultado | Evidência |
| --- | --- | --- |
| Grafo de dependências | Nove projetos; direções permitidas e domínio sem Godot | `linux/architecture.log` |
| Core | 14 testes de identidade, 90°, composição/inversa, ENU e valores inválidos | `linux/domain-tests.log`, TRX |
| Contratos/números JSON | 85 testes dos quatro produtos, parcial, prazos, unidades e parsing inteiro exato | `linux/domain-tests.log`, TRX |
| Localização pura | 18 testes de fallback/formatação, sem cultura global | `linux/domain-tests.log`, TRX |
| Catálogos/CLI | 25 testes de referências, geometria, capacidades, história e exit codes | `linux/domain-tests.log`, TRX |
| Total .NET | 142 executados/passaram, zero falhas ou ignorados | `linux/report.json`, quatro TRX |
| Instalador | 10 testes Python passaram | `linux/installer-tests.log` |
| Conteúdo válido | Exit 0, `valid:true` e nenhum diagnóstico | `linux/validate-content.log` |
| Conteúdo inválido | Exit 2 e campo `$.required_feature_ids[0]` identificado | `linux/invalid-content.log` |
| Build | Nove projetos, zero warnings/erros | `linux/build.log` |
| Integração do projeto | Markers de C#, fachada GDScript e localização PO | `linux/editor-smoke.log` |
| Exports Linux/Windows | Gerados com runtime autocontido | `linux/export-linux.log`, `linux/export-windows.log` |
| Export Linux nativo | Headless, sem SDK configurado e em namespace sem rede externa | `linux/export-smoke-linux.log` |
| Locks | 17 arquivos inalterados durante todo o pipeline | `linux/report.json` |

O relatório inclui comandos exatos, versões, estado de cada verificação e SHA256 de todos os arquivos dos exports. Binários e caches ficam fora do Git. `source-sha256.json` identifica o conteúdo dos arquivos versionados no índice Git, com a normalização de texto do Git. O log `specs-final.log` foi produzido após atualizar os estados documentais.

## Revisão automatizada

Achados corrigidos: contratos de história com parcial; afirmação documental indevida de auditoria geral de chaves; divergência entre JSON Schema e inteiros escritos como `400.0`/`4e2`. O parser compartilhado trabalha sobre os dígitos exatos para rejeitar frações minúsculas/longas e overflow sem arredondá-los. A revisão posterior de `JsonNumbers`, fachada, verificador e grafo não encontrou bloqueio concreto remanescente. Isso não substitui revisão humana.

## Pendências explícitas

Execução nativa e offline Windows, inspeção gráfica em ambos os sistemas e revisão humana. As ferramentas de desktop não estavam expostas nesta sessão Cloud; os testes da cena foram headless. AC-F01-01 e o gate M0 continuam sem aceite. A validação isolada dos schemas não declara aprovado o CQ de AC-F06-01, que depende de componentes M2.
