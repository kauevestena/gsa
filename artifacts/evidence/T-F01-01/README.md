# Evidência — T-F01-01

Estado: em revisão, sem aceite humano. Requisito REQ-F01-01; cenário relacionado AC-F01-01. Esta pasta registra o incremento local de toolchain/bootstrap, sem declarar o cenário completo da funcionalidade F01 aprovado.

Base de implementação: commit `477ae796f3fe60c8df04ad7b2254bc99132695bb` de `kauevestena/gsa`, branch local `codex/f01-toolchain-bootstrap`. O repositório já continha Git e especificações; ambos foram preservados. Sem commit ou envio ao remoto neste incremento.

## Entradas

- [Manifesto com fontes e SHA512](../../../toolchain/manifest.json), `global.json`, locks NuGet e projeto mínimo em `game/`.
- Ambiente disponível: Debian 13 x86-64; nenhum ambiente Windows nativo disponível.
- Sem seed de simulação: este bootstrap não contém sensores, observações ou gameplay.
- Godot verificado: `4.7.2.stable.mono.official.ed1daf0bf`; SDK .NET `10.0.404`; runtime exportado .NET `10.0.15`.
- Os hashes de código/configuração estão em `source-sha256.json`; os logs e hashes dos binários gerados estão em `linux/report.json`. Artefatos binários não são versionados.

## Resultados comprovados

| Verificação | Resultado | Registro |
| --- | --- | --- |
| Instalação SDK/editor/templates | SHA512 confere com fonte oficial; extração concluída | `install-linux.log` |
| Reexecução do instalador | Cache revalidado; instalações reutilizadas | `install-linux-repeat.log` |
| Segurança do instalador | 10 testes passaram, sem downloads | `installer-tests.log` |
| Specs | 76 documentos, 21 funcionalidades e 105 tarefas válidas | `linux/specs.log` |
| Restore/build Debug | Locks respeitados, zero warnings/erros | `linux/restore.log`, `linux/build.log` |
| Import e smoke do projeto | C# carregado; marcador após três frames | `linux/import.log`, `linux/editor-smoke.log` |
| Export Linux e Windows | Pacotes completos gerados em Linux | `linux/export-linux.log`, `linux/export-windows.log` |
| Export Linux nativo/offline | Headless, sem SDK configurado e sem rede externa; exit 0 | `linux/export-smoke-linux.log` |
| Locks após export | Inalterados entre início e fim da verificação | `linux/report.json` |
| Revisão automatizada do código | Dois achados corrigidos; sem bloqueio concreto remanescente | `code-review.txt` |

O primeiro export detectou a ausência da solução exigida pelo Godot; o verificador rejeitou o erro mesmo com exit code zero da engine. A solução mínima foi incluída. Os locks foram separados por configuração/RID após verificar a diferença entre dependências do editor e do export. Os logs acima correspondem à execução final bem-sucedida.

## Reprodução

Na raiz do checkout Linux:

```sh
python3 scripts/install_toolchain.py
python3 -m unittest discover -s tests/toolchain -v
python3 scripts/check_bootstrap.py --offline-smoke
```

No Windows x86-64:

```powershell
py -3 scripts/install_toolchain.py
py -3 -m unittest discover -s tests/toolchain -v
py -3 scripts/check_bootstrap.py
```

O verificador registra comando, versões, exit code, SHA256 dos exports e locks em `artifacts/logs/<host>/report.json`; rejeita erros Godot mesmo quando a engine retorna exit code zero. Cada export limpa seu diretório dedicado. O smoke exige o marcador emitido por C# após três frames, usando o runtime distribuído sem `DOTNET_ROOT`. `--offline-smoke` cria um namespace Linux sem interfaces de rede externas.

O teste headless não valida janela, GPU ou interação manual. Para revisão gráfica, abrir o export inteiro em cada OS e confirmar a tela GSA e saída por Escape. No Windows, repetir com a rede desabilitada, registrar versão do OS/driver e conservar o log do wrapper console `gsa.console.exe --headless -- --smoke-test`.

## Pendências

- Smoke nativo Windows e inicialização offline nesse sistema: pendentes por ausência de ambiente; responsável funcional: revisão técnica em Windows.
- Verificação gráfica Linux/Windows: pendente, não coberta por headless.
- Revisão humana de T-F01-01: pendente. As tarefas dependentes permanecem pendentes.
