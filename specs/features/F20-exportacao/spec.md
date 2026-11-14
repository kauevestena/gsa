# F20 — Exportações científicas — expansão E1

[Índice](../../README.md) · [Plano](plan.md) · [Tarefas](tasks.md)

Marcos: E1. Origem: Arquitetura técnica: Exportação opcional; Decisões em aberto. Estado: especificado; não implementado.

## Requisitos

### REQ-F20-01 — Exportação explícita

Modo Técnico exporta produto escolhido e revisão, sem alterar coleta/save ou revelar verdade de campo.

### REQ-F20-02 — LAS e LAZ

Exportar pontos estimados em LAS 1.4 e LAZ equivalente, com escala/unidades/metadados e licença de dependência verificadas.

### REQ-F20-03 — CSV e GeoJSON

Exportar trajetória CSV e inventário GeoJSON compatível com RFC 7946, sem rotular ENU fictício como WGS84 real.

### REQ-F20-04 — Interoperabilidade

Abrir exemplos em CloudCompare/QGIS e comparar contagens/coordenadas/campos com os produtos originais.

## Comportamento e limites

E1 é posterior à 1.0; sua implementação não bloqueia contratos internos. Exportação exige produto processado protegido e revisão escolhida. UI mostra destino, conteúdo, referencial e anonimização. Cancelamento não deixa arquivo final aparente: gerar temporário e renomear no sucesso.

LAS: versão 1.4, point format 6, escala XYZ=0,001 m, offsets locais próximos da origem, tempo GPS sintético em modo GPS Week Time (Global Encoding bit 0 = 0), valor time_s módulo 604800 e semana sintética no sidecar; intensidade e número de retorno quando disponíveis. Classificação padrão é não classificado; não exportar rótulos de verdade como classificação estimada. Salvar sidecar com datum fictício, origem, versão de modelo e receita; não inventar EPSG. LAZ deve decodificar para os mesmos inteiros/coordenadas/atributos. T-F20-01 seleciona biblioteca .NET compatível ou subprocesso empacotado após verificar licença, formato e plataformas; não embutir ferramenta não redistribuível.

CSV UTF-8 com cabeçalho `time_s,e_m,n_m,h_m,H_m,qx,qy,qz,qw,solution`; separador vírgula, ponto decimal e aspas RFC-style para strings quando necessário. Coordenadas double com precisão suficiente para round-trip.

GeoJSON usa WGS84 longitude/latitude em graus conforme [RFC 7946](https://www.rfc-editor.org/rfc/rfc7946). Para um mundo fictício, posicionamento é sintético e opt-in, ancorado em lon=-30°, lat=0°, altura elipsoidal=0. Converter ENU relativo à origem por rotação para ECEF sobre elipsoide WGS84, então ECEF→geodésicas; usar parâmetros a=6378137 m e f=1/298.257223563. A origem do mapa é transladada para essa âncora, sem afirmar que São Datum existe ali. Incluir `synthetic_world=true`, CRS local e coordenadas ENU originais nas properties; altura H vai em propriedade, coordenada Z usa altura elipsoidal sintética. Não usar membro CRS customizado para alterar o significado das coordenadas GeoJSON.

Todos os formatos exportam estimativa e incerteza disponível, nunca posição de referência oculta. Dados pessoais no produto devem estar anonimizados antes da exportação de imagens associadas.

## Interfaces

ExportRequest, ExportManifest, ProcessedProduct e adaptadores de formato; extensão versionada do menu Técnico. Ver [contratos comuns](../../interfaces.md), [modelos](../../modelos.md) e [parâmetros](../../parametros.md).

## Cenários de aceite

### AC-F20-01

Dado produto exportado/cancelado, então o original permanece com mesmo hash e não há arquivo final incompleto.

### AC-F20-02

Dados LAS e LAZ, então contagem e atributos coincidem e erro de quantização por eixo é ≤0,0005 m, salvo arredondamento numérico documentado.

### AC-F20-03

Dado inventário sintético GeoJSON, então coordenadas são longitude/latitude válidas, marca de ficção está presente e round-trip local tem erro <1 mm.

### AC-F20-04

Dados arquivos finais, então leitores independentes e CloudCompare/QGIS os abrem com geometria/unidades coerentes.
