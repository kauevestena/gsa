# F01 — Fundação e ferramentas

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: M0. Origem: Arquitetura técnica; Escolha de engine; Escopo, MVP e roadmap. Estado: fundação integrada no PR #1 após CI nativa Linux/Windows, com aceite técnico delegado; inspeção gráfica e isolamento Windows separados.

## Requisitos

### REQ-F01-01 — Toolchain verificável

Fixar uma versão estável Godot 4 .NET, SDK .NET compatível e templates Windows/Linux, com fonte oficial, versões exatas e hashes.

### REQ-F01-02 — Núcleo independente

Criar solução Core/Missions/Engine/Tools com dependências da arquitetura e testes do domínio executáveis sem Godot.

### REQ-F01-03 — Conteúdo validável

Disponibilizar CLI para validar contratos/perfis/missões e rejeitar IDs ausentes, geometria inválida, limites incoerentes e capacidade incompatível.

### REQ-F01-04 — Build reproduzível

Automatizar build, testes e exports Windows/Linux sem serviços necessários ao funcionamento do jogo.

## Comportamento e limites

M0 começou sem solução implementada; reutilizar o Git existente e manter ignores de build/cache. A autorização posterior registrada em specs/README.md permite publicar branches e PRs no remoto existente e mesclar quando a CI do SHA atual passar. Não versionar saves pessoais, caches Godot ou executáveis gerados. Commits abrangem somente arquivos revisados; a especificação original é preservada.

Escolha de versão: consultar download oficial estável Godot 4 .NET, selecionar seu patch estável mais recente e o SDK .NET LTS suportado por essa edição, testar export mínimo nos dois sistemas e registrar o resultado. Não usar beta nem copiar versões não comprovadas da v0.1. Persistir `global.json`, versão do Godot e lock de pacotes. Se o par falhar, testar o patch estável anterior e registrar a evidência, mantendo Godot 4 .NET.

CLI futura: `gsa-tools validate-content <dir>` (exit 0 válido/2 conteúdo inválido), `precompute-sky <scene-manifest> <output>`, `replay-core <manifest> <report>`, `quality-report <product> <contract> <reference>`. Os dois últimos acessam apenas arquivos declarados; diagnóstico JSON identifica caminho, campo e motivo. Mensagens para desenvolvedores não integram a tradução do jogo.

## Interfaces

Solução .NET, dados JSON e comandos de CLI; sem mudanças no modelo público de gameplay. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F01-01

Dado um ambiente limpo com dependências documentadas, quando compilar/testar/exportar, então Core passa sem engine e os executáveis mínimos abrem offline em Windows e Linux.

### AC-F01-02

Dado conteúdo com ID inexistente ou limiar impossível, quando executar validate-content, então o exit code é 2 e o campo problemático é indicado; conteúdo válido retorna 0.

### AC-F01-03

Dada a solução, quando inspecionar referências de projeto, então Core/Missions não importam Godot e o GDScript acessa o domínio pela fachada.
