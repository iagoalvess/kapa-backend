# Contrato da API

As convenções que valem para **toda** rota. Os erros têm arquivo próprio: [erros.md](erros.md).

## Nomes em snake_case

Campo de corpo, de query e de formulário, na ida e na volta: `valor_em_centavos`,
`ordenar_por`, `pago_em`. Vale também para as chaves de `errors` no 400 e para `trace_id`.

Três coisas **não** acompanham, e é de propósito:

| O quê | Como fica | Por quê |
| --- | --- | --- |
| Valor de enum | `Pendente`, `Buffet`, `Turma` | Vários são gravados como texto no banco; mudar a serialização faria o JSON discordar da coluna. Nome de campo é transporte, valor de enum é dado |
| Segmento de rota | `/parcelas/{id}/baixa-manual` | Caminho de URL é kebab-case por convenção da web, não snake_case |
| Corpo de webhook recebido | o que o provedor mandar | Quem define o formato de um webhook é quem o envia |

O corpo do evento de auditoria, gravado em `eventos`, também segue em camelCase: é registro de um
fato passado, e renomear as chaves só dos eventos novos obrigaria a consultar pelos dois nomes.

## Campo nulo aparece

Campo declarado no contrato vem sempre, com `null` quando não tem valor. Ausente quer dizer
**removido do contrato**, não "vazio".

```json
{ "pago_em": null, "fornecedor_id": null, "tem_comprovante": false }
```

É o que permite ao cliente distinguir "não informado" de "esta versão da API não manda mais esse
campo". Cliente em TypeScript deve tratar o campo como `T | null`, e não como opcional: `!== undefined`
passa direto por um `null` e estoura na linha seguinte.

`refresh_token` é o caso mais visível: com o cookie de sessão ligado — o padrão — ele vem `null`,
porque o token viaja no cookie `HttpOnly`. Não é um campo que às vezes some; é um campo que, neste
modo, não tem valor.

## Unidades no nome do campo

Dinheiro é **inteiro em centavos**, e o nome diz: `valor_em_centavos`, `pago_em_centavos`,
`saldo_projetado_em_centavos`. Nunca decimal, nunca reais.

O OpenAPI que a API publica descreve tipo, não semântica: para `{"type": "integer"}`, o nome do
campo é a única coisa que diz se `8500` são R$ 85,00 ou R$ 8.500,00. Por isso o sufixo fica, mesmo
sendo repetitivo — é documentação que não tem como ficar desatualizada.

Pela mesma razão, `carencia_em_dias` traz a unidade. As três exceções conhecidas são os
percentuais, que são **base 10.000** sem dizer no nome:

| Campo | `200` significa |
| --- | --- |
| `percentual_de_multa` | 2% |
| `percentual_de_juros_ao_mes` | 2% |
| `percentual_de_desconto_por_antecipacao` | 2% |

Inteiro, e não decimal, para `0,5%` caber sem float — `50`. Renomeá-los para carregar a base
atravessaria entidade, coluna e migration num sistema financeiro; a tabela acima é a compensação.

## Datas

Instante vai em UTC no formato ISO 8601 com `Z` (`2026-09-15T21:10:18.414Z`). Dia sem hora vai
como `aaaa-mm-dd` (`vencimento`, `pago_em`, `competencia`). A conversão para o fuso de quem olha é
da tela.

## Paginação

Query: `pagina` (começa em 1), `tamanho` (teto de 100 no servidor), `ordenar_por`, `descendente`.

Resposta: `itens`, `pagina`, `tamanho`, `total`, `total_paginas`, `tem_proxima`.

`ordenar_por` aceita só as colunas que cada listagem declara; qualquer outro valor cai na ordem
padrão dela, sem erro. Toda listagem ordena com desempate único — sem ele, dois registros de
mesmo nome trocam de página e um some.

## Versão

Na URL: `/api/v1/...`. Não há rota sem versão, e o `v1` não é opcional.
