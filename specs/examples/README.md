# Exemplos de contrato e relatório

[Interfaces](../interfaces.md) · [Modelos de CQ](../modelos.md)

- [Contrato de fachadas](contract-fachadas.json): primeira missão, sem RMSE obrigatório, 20 feições, formato interno, prazo de seis horas com tolerância de 25% e entrega parcial desabilitada.
- [Resultado e proposta de pagamento](report-fachadas.json): dados sintéticos de exemplo, sem uma execução real do jogo. Cobertura 0,94 gera margem 0,5 entre 0,90 e 0,98; imagens válidas 0,89 geram margem 0,5 entre 0,80 e 0,98. Os predicados passam. Logo q=0,5, quatro estrelas e proposta de 360 Coords sobre base 400, sem atraso.

IDs de áreas, superfícies, fachadas e chaves são contratos de conteúdo a criar nas tarefas de fundação/tutorial. Não são caminhos para assets já existentes. UUIDs são constantes de fixture, não dados pessoais. Formatação decimal permanece invariante mesmo quando a UI está em português.

Casos derivados para a implementação: cobertura 0,89 exige retrabalho; uma máscara ausente exige retrabalho; duas horas de atraso aplicam multa de 10%; após 27000 s de calendário o contrato está expirado e não paga; submeter a mesma instância duas vezes não repete a liquidação.
