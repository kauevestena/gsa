# Dependências da fundação M0

As fontes e hashes das ferramentas estão em [toolchain/manifest.json](../toolchain/manifest.json). A restauração NuGet usa exclusivamente a fonte declarada em `NuGet.Config`; versões resolvidas e hashes de conteúdo constam dos locks por projeto/configuração/RID. Não há serviço necessário à execução do jogo exportado.

| Dependência | Versão | Finalidade | Licença | Verificação |
| --- | --- | --- | --- | --- |
| Godot .NET, Godot.NET.Sdk e bindings/source generators | 4.7.2 | Engine, interop C#/GDScript, export | MIT | Build, import, fachada e smoke do export |
| SDK .NET | 10.0.404 | Compilação e ferramentas | MIT e avisos incluídos no SDK | Versão exata e restore/build/test |
| Runtime .NET exportado | 10.0.15 | Execução autocontida | MIT e avisos incluídos | Smoke sem SDK e com rede isolada em Linux |
| Microsoft.NET.Test.Sdk, TestHost, ObjectModel e CodeCoverage | 18.0.1 | Descoberta/execução xUnit e TRX | MIT | 142 testes .NET |
| xunit, assert, core, extensibility.core/execution | 2.9.3 | Fixtures analíticos e validação comportamental | Apache-2.0 | 142 testes .NET |
| xunit.runner.visualstudio | 3.1.5 | Adaptador VSTest | Apache-2.0 | Quatro suítes, TRX com execução não vazia |
| xunit.analyzers | 1.18.0 | Diagnósticos de testes na compilação | Apache-2.0 | Build sem warnings |
| xunit.abstractions | 2.0.3 | Contratos transitivos do runner | Referência de licença xUnit no pacote; Apache-2.0 do projeto | Descoberta e execução das suítes |
| Newtonsoft.Json | 13.0.3 | Dependência transitiva da plataforma de testes | MIT | Execução do VSTest; não usada pelo domínio/jogo |
| Python | 3.10+ | Scripts portáteis e testes do instalador, só stdlib | PSF | 10 testes Python e pipeline |

Os valores de licença NuGet foram conferidos nos manifests `.nuspec` dos pacotes restaurados; `xunit.abstractions` usa o campo legado `licenseUrl` apontando para a licença do projeto xUnit. O domínio usa `System.Text.Json` do .NET; não adiciona um serializador externo. A lista não atribui licença ao jogo nem resolve EXT-02. Avisos de distribuição completos serão tratados no marco de lançamento.
