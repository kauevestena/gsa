# Integração contínua

O workflow [CI](../.github/workflows/ci.yml) executa o mesmo pipeline local em runners GitHub x86-64 `ubuntu-24.04` e `windows-2025`. É disparado por pull requests, pushes para `main` e execução manual. Uma nova revisão cancela a execução anterior do mesmo PR; os dois sistemas são testados mesmo quando um deles falha.

Cada job instala Python 3.12 x64 e as ferramentas exatas de `toolchain/manifest.json`, verificando SHA512 antes da extração. Em seguida executa `python scripts/verify.py --exports host`: specs, grafo de dependências, testes Python/xUnit, CLI com entradas válidas/inválidas, build, testes headless da cena e export/smoke nativo do sistema do runner. Os locks precisam permanecer inalterados. Os executáveis exportados usam seu runtime autocontido; o processo de smoke não recebe `DOTNET_ROOT`.

Checks esperados antes de merge: **Foundation (Linux x64)** e **Foundation (Windows x64)**, ambos concluídos com sucesso na revisão atual do PR. Uma execução ausente, cancelada, ignorada ou ainda em progresso não equivale a sucesso. O job Windows usa o wrapper console do export para observar saída e exit code do processo nativo.

Os logs, relatório JSON e TRX são preservados por 14 dias em `m0-evidence-linux` e `m0-evidence-windows`, inclusive quando há falha. Se a instalação falhar antes de criar arquivos de evidência, seu diagnóstico permanece no log do próprio passo do Actions. Os executáveis não são enviados como artefatos nem publicados como release por este workflow; seus hashes constam do relatório gerado no runner.

Headless não comprova renderização gráfica. A CI também não desabilita a rede do runner: o isolamento de rede Linux é verificável localmente com `python scripts/verify.py --offline-smoke`, pois políticas de namespaces variam entre runners hospedados. A pendência offline Windows e as inspeções visuais devem continuar registradas até verificadas separadamente.

## Actions fixadas

As três actions oficiais foram consultadas em 14/11/2026 e fixadas pelo commit completo do tag estável:

| Action | Release | Commit |
| --- | --- | --- |
| `actions/checkout` | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| `actions/setup-python` | v7.1.0 | `6d4fd22b20465ccc0f7354476588e8df7c9ca0e0` |
| `actions/upload-artifact` | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` |

O `GITHUB_TOKEN` tem somente `contents: read`; credenciais de checkout não persistem. Não há segredo adicional, `pull_request_target`, aprovação automática de conteúdo ou bypass de proteção de branch. A conexão usada para publicar alterações em `.github/workflows/` precisa ter permissão de escrita de workflows; isso é separado da permissão de leitura concedida ao job.
