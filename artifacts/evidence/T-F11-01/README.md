# Evidência — T-F11-01

Estado: em revisão. REQ-F11-01 e REQ-F11-05; cenário relacionado AC-F11-01, restrito à cena mínima de M0.

Entradas: PO PT-BR/EN-US em `game/localization/`, glossário com cabeçalho normativo em `content/localization/glossary.csv`, `LocalePolicy`, `UiNumberFormatter` e `LocalizationService`. A preferência explícita de idioma é armazenada separadamente do domínio científico em `user://settings/locale.cfg`.

18 testes puros passaram. O smoke na engine e no export Linux verificou consulta real PO, singular/plural 0/1/2, contextos distintos, fallback, troca com sinal, persistência/reabertura e rejeição de escolha não suportada. O fixture escreve um arquivo temporário de preferência próprio e o remove. Coordenadas `1234.56` são apresentadas como `1234,56` / `1,234.56`; o JSON científico e culturas globais .NET permanecem iguais.

Reprodução e logs: [execução integrada](../T-F01-02/README.md), markers `GSA_LOCALIZATION_SMOKE_OK` em `../T-F01-02/linux/editor-smoke.log` e `export-smoke-linux.log`. A cena possui botões PT/EN, mas não houve inspeção gráfica. HUD/mapa/Geopédia/voz e auditoria linguística geral pertencem a marcos posteriores; AC-F11-01 completo não é declarado aceito. Sem seed; revisão humana pendente.
