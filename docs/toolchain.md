# Toolchain de desenvolvimento — T-F01-01

Versões verificadas nas fontes oficiais em 13/11/2026: **Godot 4.7.2 estável .NET**, **Godot.NET.Sdk 4.7.2** e **.NET SDK 10.0.404 LTS**, com alvo `net10.0`. O [manifesto](../toolchain/manifest.json) fixa URLs e SHA512 dos editores, templates e SDKs para Linux/Windows x86-64. `global.json` desabilita troca automática de SDK e versões de prévia.

A página oficial de download aponta 4.7.2; o release foi publicado em 18/08/2026. A documentação C# permite .NET 8 ou posterior. O índice oficial da Microsoft aponta .NET 10 como LTS ativo e SDK 10.0.404; .NET 8 encerrou suporte em 10/11/2026. A compatibilidade efetiva é demonstrada pelos logs do bootstrap, não apenas pelo atendimento à versão mínima.

## Instalação local

É necessário Python 3.10 ou posterior. O instalador usa somente a biblioteca padrão, detecta Linux/Windows x86-64, valida SHA512 **antes** de extrair e instala em `work/toolchain/`, sem alterar o sistema ou a pasta pessoal. Downloads são reutilizados somente após nova validação de hash. O pacote oficial de templates tem cerca de 1,2 GB; a extração conserva apenas os templates desktop x86-64 dos dois sistemas. Reservar pelo menos 4 GB livres para ferramentas, cache e exports.

Linux, a partir da raiz do repositório:

```sh
python3 scripts/install_toolchain.py
export DOTNET_ROOT="$PWD/work/toolchain/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export GODOT="$PWD/work/toolchain/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64"
dotnet --version
"$GODOT" --version
```

Windows, PowerShell na raiz:

```powershell
py -3 scripts/install_toolchain.py
$env:DOTNET_ROOT = "$PWD/work/toolchain/dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:GODOT = "$PWD/work/toolchain/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe"
dotnet --version
& $env:GODOT --version
```

`--skip-templates` prepara apenas edição/build. `--destination <diretório>` muda o destino; os presets do bootstrap usam o destino padrão, portanto caminhos de templates precisam ser ajustados se ele mudar. Instalação interrompida não é considerada válida. Se houver erro de checksum, remover apenas o arquivo identificado e repetir; se houver diretório de instalação sem marcador válido, movê-lo para inspeção antes de repetir. Não executar instaladores concorrentes sobre o mesmo destino.

## Dependências nativas

Usar uma distribuição Linux x86-64 suportada pelo .NET 10. No Debian/Ubuntu, o SDK/runtime precisa das bibliotecas nativas de glibc, libgcc, libstdc++, ICU, OpenSSL e zlib; os nomes exatos dependem da versão da distribuição. O editor Godot com janela exige sessão X11/Wayland e driver OpenGL 3.3 compatível com o renderer Compatibility. O smoke headless não comprova renderização com GPU. Consultar as [dependências oficiais .NET para Linux](https://learn.microsoft.com/dotnet/core/install/linux) e os [requisitos do Godot](https://docs.godotengine.org/en/stable/about/system_requirements.html).

Windows requer edição x86-64 suportada pelo .NET 10 e pelo Godot. O SDK é necessário para desenvolver; exports usam publicação autocontida para que o jogador não precise instalar SDK nem se conectar a um serviço.

## Build, testes e exports da fundação

`Gsa.sln` reúne Core/Missions/Engine/Tools, jogo e testes. `Gsa.Domain.slnf` restringe execução ao domínio, ferramentas e testes puros, sem Godot. `game/Gsa.sln` permanece como solução mínima exigida pelo exportador. A tela inicial demonstra a fachada e localização e abre o quarteirão semântico M1. O pipeline prepara o cache de céu antes do import/export; o mesmo manifesto alimenta desenho e colisão. A preferência de idioma persiste separadamente dos dados científicos.

`Directory.Build.targets` seleciona locks por configuração/RID em todos os projetos: `packages.Debug.lock.json` e, para projetos publicados, `packages.ExportRelease.linux-x64.lock.json` / `packages.ExportRelease.win-x64.lock.json`. Os grafos diferem porque o editor usa GodotSharpEditor e os exports incluem runtime específico. Uma instalação normal não regenera locks; alterar dependências exige revisão dos arquivos afetados. Versões, finalidade e licenças adicionais estão em [dependências](dependencies.md).

O comando abaixo detecta as ferramentas locais, configura o ambiente, verifica versões/specs/dependências, executa os testes e a CLI com entradas válidas/inválidas, compila, importa, executa os testes de integração C#/GDScript/PO e exporta ambos os presets. Testa nativamente somente o export do sistema em execução, sem SDK configurado no processo exportado:

```sh
python3 scripts/verify.py
```

No Windows:

```powershell
py -3 scripts/verify.py
```

Os logs, TRX e relatório ficam em `artifacts/logs/<linux|windows>/`, e os pacotes em `artifacts/exports/<linux|windows>/`. Logs de execução e cada diretório de export solicitado são limpos antes da nova execução. `--exports host` restringe ao host; `--exports none` executa testes/build e smoke do projeto. O verificador aplica `RestoreLockedMode=true` também ao publish da engine e exige locks inalterados no fim. O comando não declara teste gráfico a partir de headless. Em Linux com namespaces de usuário habilitados, `--offline-smoke` testa o export em namespace sem rede externa; sem essa opção, o isolamento não é alegado.

Para reproduzir etapas individuais, com as variáveis acima configuradas:

```sh
dotnet restore Gsa.Domain.slnf --locked-mode
dotnet test Gsa.Domain.slnf --no-restore
dotnet run --project src/Gsa.Tools -- validate-content content
dotnet restore Gsa.sln --locked-mode
dotnet build Gsa.sln --no-restore
export RestoreLockedMode=true
```

Os presets `Linux` e `Windows` usam os templates locais em `work/toolchain/templates/`. A partir da raiz, criar primeiro `artifacts/exports/linux` e `artifacts/exports/windows`, importar o projeto e exportar:

```sh
"$GODOT" --headless --editor --path game --import
"$GODOT" --headless --path game --export-release Linux "$PWD/artifacts/exports/linux/gsa.x86_64"
"$GODOT" --headless --path game --export-release Windows "$PWD/artifacts/exports/windows/gsa.exe"
```

Em PowerShell, usar `& $env:GODOT` no lugar de `"$GODOT"` e `$env:RestoreLockedMode = "true"` no lugar do comando `export`. As saídas de build, caches e ferramentas não são versionadas. Restaurar pacotes e instalar ferramentas exige rede; o projeto exportado deve iniciar offline. Export cruzado para Windows em Linux comprova a geração do pacote, mas sua execução continua exigindo smoke test no Windows. Distribuir a pasta inteira do export, com PCK e `data_Gsa_*`; o executável sozinho não basta.

Os testes do instalador usam arquivos temporários, sem downloads:

```sh
python3 -m unittest discover -s tests/toolchain -v
```

## Estado e revisão

M0 foi integrado no PR #1 com aceite técnico delegado após CI nativa Linux/Windows. O usuário autorizou desenvolvimento contínuo e merges após CI verde, conforme `specs/README.md`. A execução nativa Windows headless já foi verificada; inspeção gráfica e isolamento de rede Windows continuam separados. A aprovação do gate M1 exige também os incrementos restantes e medição na máquina de referência.

Godot e seu SDK usam licença MIT; o SDK .NET inclui licença MIT e avisos de terceiros dentro do pacote. A licença do jogo permanece pendente em EXT-02; esta seleção de ferramentas não resolve a autorização institucional de publicação.
