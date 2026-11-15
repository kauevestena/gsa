# Evidence — T-F18-02 (Adicionar álbum e cosméticos)

## Escopo
Implementação do álbum de pérolas com limite estrito de `30` fotos e confirmação explícita do usuário para substituir foto existente ao tentar salvar a 31ª entrada, exportação local em miniatura `256x144` com aplicação automática de mascaramento de privacidade (`PrivacyMaskingService`) sem mutar amostras brutas, catálogo visual com `3` pinturas, `3` adesivos, `3` buzinas (incluindo `horn_cackle`) e fantasias da Gimbalina (`GimbalinaCostumeId`), persistência via `BlooperAndCosmeticSnapshot` e garantia explícita de invariância científica no CQ (`VerifyCosmeticScientificInvariance`).

## Arquivos de Implementação e Testes
- [BlooperAlbumAndCosmetics.cs](../../../src/Gsa.Missions/Activities/BlooperAlbumAndCosmetics.cs)
- [en_US.po](../../../game/localization/en_US.po) & [pt_BR.po](../../../game/localization/pt_BR.po)
- [M4EconomyServicesAndCosmeticsTests.cs](../../../tests/Gsa.Missions.Tests/M4EconomyServicesAndCosmeticsTests.cs) (`T_F18_02_BlooperAlbumCapLocalPrivacyExportCosmeticsAndScientificInvariancePersistAcrossSaves`)

## Execução e Verificação
- `dotnet test Gsa.Domain.slnf --no-restore`: `Gsa.Missions.Tests` validou teto de 30 fotos, bloqueio e confirmação de substituição da 31ª captura, exportação com privacidade aplicada, catálogo de cosméticos e buzina de cacarejo, invariância científica no CQ/plataforma e round-trip de save.

## Estado
Aceita via revisão técnica delegada pelo proprietário do repositório após aprovação na CI do [PR #13](https://github.com/kauevestena/gsa/pull/13) (SHA testado `8591eb655b2a2bb593d752623a91f402f97aeb76`, merge `ba18ae339d025405275ac6363ab7ee1625a1cbff`, execução [43683993914](https://github.com/kauevestena/gsa/actions/runs/43683993914), jobs Linux `132720474455` e Windows `132720474804`); ver [resumo CI](../M4-wave1-ci/summary.json).

