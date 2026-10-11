# Pedidos de reembolso da assinatura

Decisão de produto de 10/10/2026: a solicitação do Presidente registra um pedido para análise
manual. O envio não chama o Mercado Pago, não cancela a recorrência, não encerra a assinatura
e não suspende a formatura.

## Solicitação

Em **Nossos planos**, o ícone de reembolso abre o formulário com motivo obrigatório, limitado
a 2.000 caracteres. O pedido continua disponível nos 7 dias do último ciclo pago, inclusive
quando a renovação já foi cancelada. O valor vem da cobrança no servidor, com o desconto de
cupom quando houver; o cliente não informa valor, cobrança, solicitante ou contato.

`POST /api/v1/formaturas/atual/assinatura/reembolsos` recebe `{"motivo":"..."}`. Somente o
Presidente da turma ativa pode solicitar. A rota anterior, `/desistir`, registra o mesmo pedido
e também exige motivo: não existe mais estorno automático pela porta da comissão.

A tabela `pedidos_de_reembolso` guarda formatura, assinatura, cobrança, solicitante, e-mail da
sessão, motivo, referência do pagamento, meio, valor efetivamente pago, data do pagamento,
datas de registro e status inicial `Pendente`. Uma trava transacional e o índice único em
`cobranca_id` impedem duplicação, inclusive em duas abas. Repetição responde
`409 reembolso.ja_solicitado`.

O `GET` da assinatura informa `pedido_de_reembolso` com protocolo, situação e data da
solicitação para o último ciclo pago. Um novo ciclo pode receber um novo pedido; o histórico
dos ciclos anteriores fica no banco. Solicitações feitas no prazo continuam registradas
depois dos 7 dias, para que possam ser analisadas posteriormente.

## Análise manual

Nesta etapa não há tela administrativa nem aprovação automática. O suporte consulta os
pedidos pendentes no banco com acesso de operação autorizado:

```sql
select id, formatura_id, assinatura_id, cobranca_id, solicitante_id,
       email_do_solicitante, motivo, valor_em_centavos, meio,
       id_do_pagamento, paga_em, criado_em, status
from pedidos_de_reembolso
where status = 'Pendente'
order by criado_em, id;
```

Após analisar o motivo e conferir a cobrança, o suporte pode executar o estorno pelo fluxo
administrativo existente. Esse fluxo continua responsável por devolver o pagamento, cancelar
a recorrência e encerrar o acesso pago. Registrar um pedido não dispara esse fluxo.

O estorno administrativo mantém as regras anteriores: o integral considera os 7 dias na data
da execução, e o proporcional depende da vigência restante. Um pedido enviado no prazo não
estende automaticamente esse limite do estorno administrativo. Nesta etapa, a análise pode
acontecer depois do prazo, mas sua conclusão financeira continua sendo uma operação manual;
uma futura tela de aprovação deverá considerar a data da solicitação.

O pedido deve ser marcado manualmente no banco como `Concluido` após a devolução efetiva,
ou `Recusado` após a análise, atualizando também `atualizado_em`. O estorno administrativo
ainda não atualiza automaticamente o status do pedido. Nenhuma dessas etapas ocorre
somente pela solicitação do Presidente.

## Banco e retenção

A migration `RegistraPedidosDeReembolso` cria a tabela e seus índices. Aplicá-la junto da
publicação do backend. Os pedidos usam o isolamento global por formatura e ficam na lista
de dados mantidos pela retenção, junto da assinatura e do histórico de pagamentos.
