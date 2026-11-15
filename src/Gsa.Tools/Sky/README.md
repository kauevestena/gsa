# Pré-cálculo de céu — T-F02-02

`precompute-sky <scene-manifest> <output>` lê somente o manifesto neutro indicado e escreve um cache JSON, sem abrir Godot. O manifesto é a mesma fonte usada pelo graybox e pelos colliders. A CLI retorna 0 em sucesso, 2 para entrada/saída inválida e 1 para uso incorreto, com diagnósticos JSON de caminho, campo e motivo.

## Modelo inicial

O algoritmo `fibonacci_hemisphere_aabb_v1` usa 32 direções fixas no hemisfério ENU superior. Para direção `i`, `u=(i+0.5)/N`, `z=u`, raio horizontal `sqrt(1-u²)` e azimute `i*pi*(3-sqrt(5))`. A distribuição é uniforme em ângulo sólido, inclui direções próximas do horizonte e não depende de RNG nem da renderização.

A grade parte do mínimo leste/norte dos limites da cena, com células de 10×10 m. Células finais são recortadas aos limites e amostradas no centro da parte útil. A altura de referência é `origin_enu.Z + 2 m`; não se presume que a mesma grade serve a outro piso ou a um túnel em altura diferente.

Cada raio é testado contra as caixas estáticas com `collision:true`, em double. Um bloqueio com distância positiva acima de `1e-6 m` obstrui aquela direção. Estar dentro de um sólido bloqueia as direções que atravessam o seu interior; sair da face superior de um sólido não cria um autobloqueio de distância zero. Objetos dinâmicos e proxies sem colisão não bloqueiam o pré-cálculo. O modelo trata caixas como sólidos opacos para visibilidade; não simula propagação RF, difração nem transparência física por material.

`openness = direções livres / direction_count`. Céu aberto resulta em 1; um interior cercado por teto e paredes resulta em 0. DOP e observações GNSS são calculados pelo seu módulo, não fabricados pelo cache.

## Cache e consulta

`SkyVisibilityCache` registra schema/algoritmo, cena/mapa/datum, origem/limites, hash canônico do manifesto, tamanho/altura da grade, número de direções e células com índices, centro ENU e abertura. Coleções são somente leitura; parsing rejeita valores não finitos, células ausentes/duplicadas, centros incoerentes e aberturas que não sejam uma fração do número de direções.

`ValidateFor(manifest)` lança `FormatException` quando versões, datum, origem, limites ou hash não correspondem. O hash canônico do manifesto ignora formatação/ordem de propriedades, mas inclui geometria, poses e semântica. Um cache carregado deve passar essa validação antes da consulta.

`TryGetCell(position, out cell, maxHeightDeltaM: 0.5)` usa limites horizontais meio-abertos `[min,max)` e retorna false fora da grade ou da faixa de altura. Não retorna abertura fictícia quando não há célula aplicável. O adaptador continua responsável por obstruções móveis e exceções de altura/streaming.

As APIs aceitam configurações de grade alternativas explícitas, limitadas a 1–4096 direções e no máximo um milhão de células para impedir uma alocação acidental sem limite. Esses limites de ferramenta não alteram o baseline de 10 m, 2 m e 32 direções.

## Escrita e evidências

A ferramenta calcula e valida tudo antes de substituir a saída; escreve primeiro um arquivo temporário no mesmo diretório e faz a substituição atômica. Entrada e saída iguais são rejeitadas. Testes puros cobrem aberto/túnel/fachada, bordas e células parciais, round-trip JSON, cache adulterado e invalidação por versão/geometria. Testes da CLI cobrem criação e preservação da saída em caso de erro.
