# Erros da API

Toda falha desta API responde no formato [RFC 9457 — Problem Details](https://www.rfc-editor.org/rfc/rfc9457),
com `Content-Type: application/problem+json`:

```json
{
  "type": "https://github.com/kapa/backend/blob/main/docs/erros.md#pagamento.parcela_paga",
  "title": "Essa parcela já está paga. Se pagou de novo, fale com a tesouraria.",
  "status": 409,
  "detail": "Essa parcela já está paga. Se pagou de novo, fale com a tesouraria.",
  "instance": "POST /api/v1/parcelas/01a0.../informes",
  "codigo": "pagamento.parcela_paga",
  "trace_id": "0HNOJDV71F2N7:00000001"
}
```

## Ramifique por `codigo`, nunca por `title`

`codigo` é contrato estável, no formato `recurso.motivo`. Ele nunca é renomeado — renomear
partiria em duas a série histórica de quem já trata o erro.

`title` e `detail` são texto para pessoa, escritos para caber na tela, e mudam sempre que a
redação melhorar. Cliente que compara mensagem quebra na primeira revisão de texto.

`trace_id` é o identificador da requisição no log do servidor. É o que se manda junto ao abrir um
chamado — com ele a falha é encontrada em segundos.

## Os seis tipos

O tipo do erro decide o status HTTP. São seis, e é tudo:

| Status | Significa | O que o cliente faz |
| --- | --- | --- |
| **400** | O corpo não passou na validação de forma | Acende o erro no campo. `errors` traz a lista por campo, em snake_case |
| **401** | Sem credencial, ou credencial vencida | Renova a sessão; se falhar, manda para o login |
| **403** | Autenticado, mas sem permissão para isto | Não repete o pedido. `formatura.nao_selecionada` é a exceção: leva à seleção de turma |
| **404** | Não existe — **ou não é seu** | Trata como inexistente. A API responde igual nos dois casos de propósito: distinguir transformaria o endpoint num verificador de identificadores |
| **409** | Existe, mas o estado atual não permite | Mostra o motivo e recarrega: o dado mudou desde que a tela o leu |
| **503** | Dependência fora do ar | Tenta de novo mais tarde. Nada foi gravado |

O 400 de validação vem com `errors`, um mapa de campo para mensagens:

```json
{
  "status": 400,
  "codigo": "validacao.invalido",
  "errors": {
    "nova_senha": ["A senha deve ter no mínimo 8 caracteres.", "A senha deve conter ao menos um número."]
  }
}
```

Fora dos seis, um **429** com `codigo` `rate_limit.excedido` (ou `loja.fila_cheia`, a fila da loja) e o cabeçalho `Retry-After` em
segundos, e um **500** com `codigo` `erro.inesperado` — este último é sempre bug ou indisponibilidade
nossa, e o `trace_id` é o que o resolve.

## Códigos por recurso

### adesao

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="adesao.cadastro_incompleto"></a>`adesao.cadastro_incompleto` | 409 | Para aderir, informe no seu cadastro o nome completo, o CPF e a data de nascimento. |
| <a id="adesao.codigo_invalido"></a>`adesao.codigo_invalido` | 409 | O código não confere ou já expirou. Peça um código novo e use o mais recente que chegou no seu e-mail. |
| <a id="adesao.cpf_em_uso"></a>`adesao.cpf_em_uso` | 409 | Este CPF já está na adesão de outra pessoa da turma. Confira o seu cadastro ou fale com a comissão. |
| <a id="adesao.ja_aderiu"></a>`adesao.ja_aderiu` | 409 | Você já aderiu a esta versão do termo. |
| <a id="adesao.menor_de_idade"></a>`adesao.menor_de_idade` | 409 | Quem tem menos de 18 anos adere com a comissão, junto com o responsável legal, e não pela plataforma. |
| <a id="adesao.nao_encontrada"></a>`adesao.nao_encontrada` | 404 | Adesão não encontrada. |
| <a id="adesao.sem_plano_vigente"></a>`adesao.sem_plano_vigente` | 409 | A turma ainda não tem plano de cobrança em vigor. |
| <a id="adesao.sem_termo_publicado"></a>`adesao.sem_termo_publicado` | 409 | A comissão ainda não publicou o termo de adesão da turma. |
| <a id="adesao.termo_desatualizado"></a>`adesao.termo_desatualizado` | 409 | O termo ou o plano mudou enquanto você lia. Confira a versão atual antes de aceitar. |
| <a id="adesao.termo_sem_mudanca"></a>`adesao.termo_sem_mudanca` | 409 | Este texto é igual ao da versão vigente. |

### agenda

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="agenda.evento_nao_encontrado"></a>`agenda.evento_nao_encontrado` | 404 | Evento não encontrado na agenda. |

### arquivo

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="arquivo.armazenamento_esgotado"></a>`arquivo.armazenamento_esgotado` | 409 | O envio de arquivos está indisponível no momento. Tente novamente mais tarde. |
| <a id="arquivo.conteudo_indisponivel"></a>`arquivo.conteudo_indisponivel` | 404 | O conteúdo deste arquivo não está disponível. |
| <a id="arquivo.conteudo_invalido"></a>`arquivo.conteudo_invalido` | 400 | O conteúdo do arquivo não é do tipo que a extensão diz. Envie o arquivo original. |
| <a id="arquivo.cota_excedida"></a>`arquivo.cota_excedida` | 409 | Este arquivo ultrapassa sua cota de … MB. Remova outros arquivos para liberar espaço. |
| <a id="arquivo.limite_de_quantidade"></a>`arquivo.limite_de_quantidade` | 409 | Você atingiu o limite de … arquivos. Remova algum antes de enviar outro. |
| <a id="arquivo.nao_encontrado"></a>`arquivo.nao_encontrado` | 404 | Arquivo não encontrado. |

### assinatura

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="assinatura.ja_ativa"></a>`assinatura.ja_ativa` | 409 | Esta formatura já tem uma assinatura ativa. |
| <a id="assinatura.nao_ativa"></a>`assinatura.nao_ativa` | 409 | Só uma assinatura ativa pode ser cancelada. |
| <a id="assinatura.nao_encontrada"></a>`assinatura.nao_encontrada` | 404 | Esta formatura ainda não contratou um plano. |
| <a id="assinatura.plano_invalido"></a>`assinatura.plano_invalido` | 400 | Plano não encontrado. |
| <a id="assinatura.plano_menor_que_a_turma"></a>`assinatura.plano_menor_que_a_turma` | 409 | A turma já tem … pessoas e o plano … comporta …. Escolha um plano maior. |
| <a id="assinatura.transicao_invalida"></a>`assinatura.transicao_invalida` | 409 | Uma assinatura … não pode passar para …. |

### auth

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="auth.conta_bloqueada"></a>`auth.conta_bloqueada` | 403 | Conta temporariamente bloqueada por excesso de tentativas. Tente mais tarde. |
| <a id="auth.conta_desativada"></a>`auth.conta_desativada` | 403 | Esta conta está desativada. Procure um administrador. |
| <a id="auth.credenciais_invalidas"></a>`auth.credenciais_invalidas` | 401 | E-mail ou senha incorretos. |
| <a id="auth.email_nao_confirmado"></a>`auth.email_nao_confirmado` | 403 | Confirme seu e-mail antes de entrar. Verifique sua caixa de entrada. |
| <a id="auth.sessao_invalida"></a>`auth.sessao_invalida` | 401 | Sessão expirada. Faça login novamente. |

### cobranca

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="cobranca.adesao_duplicada"></a>`cobranca.adesao_duplicada` | 409 | Este plano já tem uma taxa de adesão. |
| <a id="cobranca.antecedencia_obrigatoria"></a>`cobranca.antecedencia_obrigatoria` | 400 | Diga com quantos dias de antecedência o desconto vale — senão ele sai para quem pagar um dia antes. |
| <a id="cobranca.credito_acima_do_pago"></a>`cobranca.credito_acima_do_pago` | 400 | O crédito não pode passar do que o formando já pagou neste pedido. |
| <a id="cobranca.credito_da_tesouraria"></a>`cobranca.credito_da_tesouraria` | 403 | Só a tesouraria lança crédito de um pedido já pago. |
| <a id="cobranca.credito_invalido"></a>`cobranca.credito_invalido` | 400 | O crédito não pode ser negativo. |
| <a id="cobranca.dia_invalido"></a>`cobranca.dia_invalido` | 400 | O vencimento vai do dia 1 ao 31. |
| <a id="cobranca.estoque_esgotado"></a>`cobranca.estoque_esgotado` | 409 | Não há mais unidades deste item disponíveis. |
| <a id="cobranca.estoque_menor_que_reservado"></a>`cobranca.estoque_menor_que_reservado` | 409 | Já foram pedidas … unidades deste item. Cancele pedidos antes de reduzir o estoque. |
| <a id="cobranca.grade_depois_da_festa"></a>`cobranca.grade_depois_da_festa` | 400 | A última parcela do convite extra venceria depois do fechamento da lista de convidados, 24 h antes da festa (Sprint 21, P2.1). |
| <a id="cobranca.item_com_pedido"></a>`cobranca.item_com_pedido` | 409 | Este item já foi pedido por alguém e não pode ser excluído. Encerre-o: ele para de aceitar pedidos e o que já foi pedido fica. |
| <a id="cobranca.item_da_festa_invalido"></a>`cobranca.item_da_festa_invalido` | 400 | Escolha um item da festa que exista, esteja de pé e seja rateado por formando. |
| <a id="cobranca.item_em_uso"></a>`cobranca.item_em_uso` | 409 | Este item já gerou parcelas: só o preço e a descrição podem mudar. Para mudar o resto, encerre-o e cadastre outro. |
| <a id="cobranca.item_encerrado"></a>`cobranca.item_encerrado` | 409 | Este item não está mais à venda. |
| <a id="cobranca.item_nao_e_opcional"></a>`cobranca.item_nao_e_opcional` | 400 | Este item é do plano da turma e não se pede — ele já está no seu extrato. |
| <a id="cobranca.item_nao_encontrado"></a>`cobranca.item_nao_encontrado` | 404 | Item opcional não encontrado. |
| <a id="cobranca.limite_do_item_excedido"></a>`cobranca.limite_do_item_excedido` | 409 | Cada formando pode pedir no máximo … unidades deste item. |
| <a id="cobranca.origem_obrigatoria"></a>`cobranca.origem_obrigatoria` | 400 | Informe onde a turma decidiu esta cobrança — a assembleia e a data. |
| <a id="cobranca.parcelas_acima_do_teto"></a>`cobranca.parcelas_acima_do_teto` | 400 | Este item pode ser pago em até …×. |
| <a id="cobranca.pedido_com_parcela_paga"></a>`cobranca.pedido_com_parcela_paga` | 409 | Este pedido já tem parcela paga. Fale com a tesouraria: o cancelamento agora é dela. |
| <a id="cobranca.pedido_fora_do_prazo"></a>`cobranca.pedido_fora_do_prazo` | 409 | O prazo para pedir este item já passou. |
| <a id="cobranca.pedido_nao_encontrado"></a>`cobranca.pedido_nao_encontrado` | 404 | Pedido não encontrado. |
| <a id="cobranca.pedido_sem_adesao"></a>`cobranca.pedido_sem_adesao` | 409 | Aceite o termo da turma antes de pedir: é ele que cria a sua conta de cobrança. |
| <a id="cobranca.plano_ja_vigente"></a>`cobranca.plano_ja_vigente` | 409 | Este plano já está em vigor. |
| <a id="cobranca.plano_nao_encontrado"></a>`cobranca.plano_nao_encontrado` | 404 | Plano de cobrança não encontrado. |
| <a id="cobranca.plano_sem_itens"></a>`cobranca.plano_sem_itens` | 409 | Inclua ao menos um item antes de colocar o plano em vigor. |
| <a id="cobranca.plano_vigente_existente"></a>`cobranca.plano_vigente_existente` | 409 | A turma já tem um plano em vigor. Só um vale por vez. |
| <a id="cobranca.prazo_antes_da_abertura"></a>`cobranca.prazo_antes_da_abertura` | 400 | O prazo para pedir não pode ser anterior à abertura das vendas. |
| <a id="cobranca.rateio_retroativo"></a>`cobranca.rateio_retroativo` | 400 | O rateio não pode começar num mês que já passou: a parcela nasceria vencida, com multa e juros. |
| <a id="cobranca.sem_plano_vigente"></a>`cobranca.sem_plano_vigente` | 409 | A turma ainda não tem plano de cobrança em vigor. |
| <a id="cobranca.tipo_invalido"></a>`cobranca.tipo_invalido` | 400 | Este tipo de cobrança não cabe aqui. |
| <a id="cobranca.valor_invalido"></a>`cobranca.valor_invalido` | 400 | Informe um valor maior que zero. |
| <a id="cobranca.item_da_loja"></a>`cobranca.item_da_loja` | 409 | Este item é vendido pela loja da turma. Compre pelo link da loja (Sprint 26, P8). |
| <a id="cobranca.venda_nao_aberta"></a>`cobranca.venda_nao_aberta` | 409 | As vendas deste item ainda não abriram. |

### comunicacao

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="comunicacao.arquivo_grande"></a>`comunicacao.arquivo_grande` | 400 | O documento excede o limite de … MB. |
| <a id="comunicacao.arquivo_obrigatorio"></a>`comunicacao.arquivo_obrigatorio` | 400 | Anexe o arquivo do documento. |
| <a id="comunicacao.aviso_nao_encontrado"></a>`comunicacao.aviso_nao_encontrado` | 404 | Aviso não encontrado. |
| <a id="comunicacao.documento_nao_encontrado"></a>`comunicacao.documento_nao_encontrado` | 404 | Documento não encontrado. |
| <a id="comunicacao.limite_de_fixados"></a>`comunicacao.limite_de_fixados` | 409 | Já há … avisos fixados. Desafixe um antes de fixar outro. |
| <a id="comunicacao.tipo_invalido"></a>`comunicacao.tipo_invalido` | 400 | Envie o documento em PDF, imagem (PNG, JPG ou WebP), Word (.docx) ou Excel (.xlsx). |

### conta

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="conta.link_invalido"></a>`conta.link_invalido` | 400 | Este link é inválido ou expirou. Solicite um novo. |

### convite

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="convite.email_divergente"></a>`convite.email_divergente` | 403 | Este convite é pessoal e foi enviado para …. Entre com a conta desse e-mail para aceitar. |
| <a id="convite.email_nao_confirmado"></a>`convite.email_nao_confirmado` | 403 | Confirme seu e-mail para aceitar este convite pessoal. O link de confirmação foi enviado para …. |
| <a id="convite.esgotado"></a>`convite.esgotado` | 409 | Este convite acabou de atingir o limite de entradas. Peça um novo à comissão. |
| <a id="convite.formatura_nao_contratada"></a>`convite.formatura_nao_contratada` | 403 | Contrate um plano para convidar formandos. Antes disso, dá para convidar a comissão. |
| <a id="convite.invalido"></a>`convite.invalido` | 404 | Este convite não está mais disponível. Peça um novo à comissão. |
| <a id="convite.ja_vinculado"></a>`convite.ja_vinculado` | 409 | Você já participa desta formatura. |
| <a id="convite.link_so_para_formando"></a>`convite.link_so_para_formando` | 400 | O link da turma só convida formandos. Para a comissão, envie o convite por e-mail. |
| <a id="convite.membro_desligado"></a>`convite.membro_desligado` | 403 | Você foi desligado desta formatura e o desligamento é definitivo. Fale com a comissão. |
| <a id="convite.nao_encontrado"></a>`convite.nao_encontrado` | 404 | Convite não encontrado nesta formatura. |
| <a id="convite.papel_invalido"></a>`convite.papel_invalido` | 400 | Papel inválido. Use Presidente, Tesoureiro, Comissao ou Formando. |
| <a id="convite.papel_restrito"></a>`convite.papel_restrito` | 403 | Só o Presidente convida para a comissão e a tesouraria. |
| <a id="convite.vinculo_removido"></a>`convite.vinculo_removido` | 403 | Você foi removido desta formatura. Para voltar, peça à comissão um convite pessoal. |

### festa

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="festa.convite_nao_encontrado"></a>`festa.convite_nao_encontrado` | 404 | Convite não encontrado. Genérico de propósito: código inexistente, assinatura adulterada, convite revogado, convite ainda "a definir" (sem convidado, não há ingresso) e convite de outra turma respondem igual na página pública. |
| <a id="festa.convite_revogado"></a>`festa.convite_revogado` | 409 | O convite foi revogado (estorno, cancelamento, transferência ou reemissão); a mensagem traz o motivo. |
| <a id="festa.convite_sem_titular"></a>`festa.convite_sem_titular` | 409 | O convite ainda não tem nome e documento do convidado — não entra como anônimo (P5). |
| <a id="festa.disputa_encerrada"></a>`festa.disputa_encerrada` | 409 | Este item já foi contratado ou cancelado: a escolha da turma já aconteceu. |
| <a id="festa.documento_nao_encontrado"></a>`festa.documento_nao_encontrado` | 400 | Documento não encontrado no acervo, ou visível apenas para a comissão. |
| <a id="festa.documento_obrigatorio"></a>`festa.documento_obrigatorio` | 400 | Depois do fechamento da lista, o convite precisa de nome e documento. |
| <a id="festa.email_nao_confirmado"></a>`festa.email_nao_confirmado` | 403 | Confirme seu e-mail para votar. Use o link que enviamos quando você criou a conta, ou peça outro em Minha conta. |
| <a id="festa.entrada_nao_encontrada"></a>`festa.entrada_nao_encontrada` | 404 | Entrada da portaria não encontrada. |
| <a id="festa.evento_incompleto"></a>`festa.evento_incompleto` | 409 | A festa precisa estar na agenda com data, hora e local antes de qualquer convite sair (P6). |
| <a id="festa.fora_da_janela"></a>`festa.fora_da_janela` | 409 | Fora do horário da portaria: a validação vale de 6 h antes a 12 h depois do evento (P7). |
| <a id="festa.identificacao_em_uso"></a>`festa.identificacao_em_uso` | 409 | Já existe uma mesa com esse nome na turma (sem diferenciar caixa). |
| <a id="festa.item_cancelado"></a>`festa.item_cancelado` | 409 | Este item foi cancelado. Reative-o antes de alterá-lo. |
| <a id="festa.item_com_opcional"></a>`festa.item_com_opcional` | 409 | Este item está à venda como opcional. Encerre a venda dele antes de desistir do item. |
| <a id="festa.item_em_uso"></a>`festa.item_em_uso` | 409 | Este item já tem despesa lançada. Cancele-o em vez de excluí-lo. |
| <a id="festa.item_nao_cancelado"></a>`festa.item_nao_cancelado` | 409 | Este item não está cancelado. |
| <a id="festa.item_nao_encontrado"></a>`festa.item_nao_encontrado` | 404 | Item da festa não encontrado. |
| <a id="festa.ja_validado"></a>`festa.ja_validado` | 409 | O convite já entrou. `dados` traz `check_in_id`, `validado_em`, `validado_por` e `validado_por_usuario_id` — é informação para a portaria, não erro. |
| <a id="festa.lista_fechada"></a>`festa.lista_fechada` | 409 | A lista de convidados fechou 24 h antes do evento; agora só a comissão altera o nome. |
| <a id="festa.mesa_com_dono"></a>`festa.mesa_com_dono` | 409 | A mesa já é de um formando: solte o dono antes de excluir. |
| <a id="festa.mesa_nao_encontrada"></a>`festa.mesa_nao_encontrada` | 404 | Mesa não encontrada. |
| <a id="festa.mesa_reservada"></a>`festa.mesa_reservada` | 409 | Mesa reservada não tem dono: atribuir dono a ela, ou reservar uma mesa com dono (Sprint 27, P3). |
| <a id="festa.mesas_alem_do_pedido"></a>`festa.mesas_alem_do_pedido` | 409 | O formando já tem no mapa todas as mesas que os pedidos confirmados do opcional Mesa dão direito (Sprint 27, decisão 6). |
| <a id="festa.outro_evento"></a>`festa.outro_evento` | 409 | O convite é de outro evento da turma — não é código inválido (decisão 13). |
| <a id="festa.pedido_sem_convite"></a>`festa.pedido_sem_convite` | 409 | O pedido não é de convite extra. |
| <a id="festa.proposta_de_outro_item"></a>`festa.proposta_de_outro_item` | 409 | Essa proposta é de outro item. |
| <a id="festa.proposta_nao_encontrada"></a>`festa.proposta_nao_encontrada` | 404 | Proposta não encontrada. |
| <a id="festa.sincronizacao_grande"></a>`festa.sincronizacao_grande` | 400 | Envie no máximo 2.000 entradas sem rede por vez. |

### financeiro

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="financeiro.comprovante_invalido"></a>`financeiro.comprovante_invalido` | 400 | Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP). |
| <a id="financeiro.comprovante_obrigatorio"></a>`financeiro.comprovante_obrigatorio` | 400 | Anexe o comprovante do pagamento: é ele que sustenta a prestação de contas. |
| <a id="financeiro.despesa_cancelada"></a>`financeiro.despesa_cancelada` | 409 | Esta despesa foi cancelada. Lance uma nova. |
| <a id="financeiro.despesa_duplicada"></a>`financeiro.despesa_duplicada` | 409 | Esta despesa já foi lançada para este vencimento. Confira a lista antes de lançar de novo. |
| <a id="financeiro.despesa_nao_encontrada"></a>`financeiro.despesa_nao_encontrada` | 404 | Despesa não encontrada. |
| <a id="financeiro.despesa_nao_prevista"></a>`financeiro.despesa_nao_prevista` | 409 | Esta despesa não está prevista para pagamento. |
| <a id="financeiro.documento_nao_encontrado"></a>`financeiro.documento_nao_encontrado` | 400 | Documento não encontrado no acervo, ou visível apenas para a comissão. |
| <a id="financeiro.fornecedor_em_uso"></a>`financeiro.fornecedor_em_uso` | 409 | Este fornecedor já tem despesa lançada. Desative o cadastro em vez de excluí-lo. |
| <a id="financeiro.fornecedor_nao_encontrado"></a>`financeiro.fornecedor_nao_encontrado` | 400 | Fornecedor não encontrado. |
| <a id="financeiro.fornecedor_nome_em_uso"></a>`financeiro.fornecedor_nome_em_uso` | 409 | A turma já tem um fornecedor com este nome. |
| <a id="financeiro.item_da_festa_nao_encontrado"></a>`financeiro.item_da_festa_nao_encontrado` | 400 | Item da festa não encontrado. |
| <a id="financeiro.outra_receita_cancelada"></a>`financeiro.outra_receita_cancelada` | 409 | Esta receita foi cancelada. Lance uma nova. |
| <a id="financeiro.outra_receita_data_futura"></a>`financeiro.outra_receita_data_futura` | 400 | Receita recebida não pode ter data no futuro. |
| <a id="financeiro.outra_receita_duplicada"></a>`financeiro.outra_receita_duplicada` | 409 | Esta receita já foi lançada para esta data. Confira a lista antes de lançar de novo. |
| <a id="financeiro.outra_receita_ja_recebida"></a>`financeiro.outra_receita_ja_recebida` | 409 | Esta receita já foi recebida. Para acertar algum dado, edite-a. |
| <a id="financeiro.outra_receita_nao_encontrada"></a>`financeiro.outra_receita_nao_encontrada` | 404 | Receita não encontrada. |
| <a id="financeiro.sem_comprovante"></a>`financeiro.sem_comprovante` | 404 | Esta despesa não tem comprovante. |

### formando

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="formando.nao_encontrado"></a>`formando.nao_encontrado` | 404 | Formando não encontrado nesta formatura. |

### formatura

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="formatura.assinatura_ativa"></a>`formatura.assinatura_ativa` | 409 | Cancele a renovação da assinatura antes de encerrar a formatura. |
| <a id="formatura.encerrada"></a>`formatura.encerrada` | 409 | Esta turma não contrata assinatura. |
| <a id="formatura.gratuita_pendente"></a>`formatura.gratuita_pendente` | 409 | Você já tem uma turma no plano gratuito. Contrate ou descarte essa antes de criar outra. |
| <a id="formatura.inativa"></a>`formatura.inativa` | 403 | Esta formatura está em modo leitura e não aceita alterações. |
| <a id="formatura.ja_contratada"></a>`formatura.ja_contratada` | 409 | Esta turma já contratou um plano. Encerre a turma em vez de descartá-la. |
| <a id="formatura.membro_ja_desligado"></a>`formatura.membro_ja_desligado` | 409 | Esta pessoa já foi desligada da turma. |
| <a id="formatura.membro_sem_adesao"></a>`formatura.membro_sem_adesao` | 409 | Esta pessoa ainda não aderiu ao termo e não deve nada à turma. Use Remover. |
| <a id="formatura.nao_encontrada"></a>`formatura.nao_encontrada` | 404 | Formatura não encontrada. |
| <a id="formatura.sem_vinculo"></a>`formatura.sem_vinculo` | 403 | Você não participa desta formatura. |
| <a id="formatura.transicao_invalida"></a>`formatura.transicao_invalida` | 409 | Uma formatura … não pode passar para …. |
| <a id="formatura.ultimo_presidente"></a>`formatura.ultimo_presidente` | 409 | A formatura precisa de ao menos um presidente ativo. Promova outra pessoa antes. |
| <a id="formatura.vinculo_nao_encontrado"></a>`formatura.vinculo_nao_encontrado` | 404 | Você não é membro ativo desta turma. |

### legal

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="legal.consentimento_ja_revogado"></a>`legal.consentimento_ja_revogado` | 409 | Este consentimento já foi revogado. |
| <a id="legal.consentimento_nao_encontrado"></a>`legal.consentimento_nao_encontrado` | 404 | Registro de consentimento não encontrado. |
| <a id="legal.documento_nao_encontrado"></a>`legal.documento_nao_encontrado` | 404 | Documento não encontrado. |
| <a id="legal.versao_desatualizada"></a>`legal.versao_desatualizada` | 409 | Este documento foi atualizado. Leia e aceite a versão vigente. |

### loja

A loja pública da Sprint 26. As rotas são anônimas: 404 vale para link errado, antigo ou inexistente.

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="loja.compra_nao_encontrada"></a>`loja.compra_nao_encontrada` | 404 | Não encontramos esta compra — o link é antigo, errado ou foi substituído por um reenvio. |
| <a id="loja.cpf_invalido"></a>`loja.cpf_invalido` | 400 | CPF com dígito verificador inválido; recusado antes da reserva (P6). |
| <a id="loja.dados_ainda_necessarios"></a>`loja.dados_ainda_necessarios` | 409 | Os dados do comprador sustentam os convites e a devolução até a festa. |
| <a id="loja.esgotado"></a>`loja.esgotado` | 409 | Os convites esgotaram — ou restam menos que a quantidade pedida. Responde antes da fila (decisão 8). |
| <a id="loja.fila_cheia"></a>`loja.fila_cheia` | 429 | Muita gente comprando na mesma turma. Tente de novo depois do <code>Retry-After</code>, com a mesma chave de idempotência. |
| <a id="loja.item_nao_encontrado"></a>`loja.item_nao_encontrado` | 404 | Este convite não está à venda nesta loja. |
| <a id="loja.limite_por_pessoa"></a>`loja.limite_por_pessoa` | 409 | O CPF já tem o máximo de convites deste tipo (P3). |
| <a id="loja.nao_encontrada"></a>`loja.nao_encontrada` | 404 | A turma não existe, não está ativa ou não vende nada pela loja. |
| <a id="loja.reserva_no_fim"></a>`loja.reserva_no_fim` | 409 | Não dá mais para gerar o pagamento desta reserva. Faça uma compra nova. |
| <a id="loja.sem_mercado_pago"></a>`loja.sem_mercado_pago` | 409 | A loja pública precisa do Mercado Pago da turma conectado. |
| <a id="loja.sem_pagamento"></a>`loja.sem_pagamento` | 409 | A loja está sem meio de pagamento agora. |
| <a id="loja.so_convite"></a>`loja.so_convite` | 400 | A loja pública vende só o convite da festa (tipo <code>ConviteExtra</code>). |

### membro

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="membro.detalhe_obrigatorio"></a>`membro.detalhe_obrigatorio` | 400 | Diga qual foi o motivo. |
| <a id="membro.motivo_invalido"></a>`membro.motivo_invalido` | 400 | Motivo inválido. Escolha um da lista. |
| <a id="membro.nao_encontrado"></a>`membro.nao_encontrado` | 404 | Membro não encontrado nesta formatura. |
| <a id="membro.papel_invalido"></a>`membro.papel_invalido` | 400 | Papel inválido. Use Presidente, Tesoureiro, Comissao ou Formando. |

### notificacao

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="notificacao.cobranca_obrigatoria"></a>`notificacao.cobranca_obrigatoria` | 409 | O aviso de parcela é comunicação do termo de adesão e não pode ser desligado. |
| <a id="notificacao.ja_cobrada_hoje"></a>`notificacao.ja_cobrada_hoje` | 409 | Esta parcela já foi cobrada hoje. |
| <a id="notificacao.parcela_nao_cobravel"></a>`notificacao.parcela_nao_cobravel` | 409 | Esta parcela não pode ser cobrada agora: ela já foi paga, foi cancelada ou tem um aviso de pagamento esperando conferência. |
| <a id="notificacao.regra_nao_encontrada"></a>`notificacao.regra_nao_encontrada` | 404 | Degrau da régua não encontrado. |
| <a id="notificacao.sem_degrau"></a>`notificacao.sem_degrau` | 409 | A régua não tem nenhum degrau ativo para usar nesta cobrança. |
| <a id="notificacao.sem_vinculo"></a>`notificacao.sem_vinculo` | 403 | Você não participa desta formatura. |

### pagamento

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="pagamento.cartao_recusado"></a>`pagamento.cartao_recusado` | 409 | O cartão foi recusado pelo emissor ou pelo Mercado Pago. Confira os dados, tente outro cartão ou pague pelo PIX (Sprint 35; a tela chega na 37). |
| <a id="pagamento.comprovante_invalido"></a>`pagamento.comprovante_invalido` | 400 | Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP). |
| <a id="pagamento.informe_ja_conferido"></a>`pagamento.informe_ja_conferido` | 409 | Este aviso de pagamento já foi conferido. |
| <a id="pagamento.informe_nao_encontrado"></a>`pagamento.informe_nao_encontrado` | 404 | Aviso de pagamento não encontrado. |
| <a id="pagamento.informe_pendente"></a>`pagamento.informe_pendente` | 409 | Você já avisou o pagamento de uma dessas parcelas. A tesouraria vai conferir, e você recebe um e-mail quando for confirmado. |
| <a id="pagamento.parcela_nao_aberta"></a>`pagamento.parcela_nao_aberta` | 409 | Esta parcela não está em aberto. |
| <a id="pagamento.parcela_nao_encontrada"></a>`pagamento.parcela_nao_encontrada` | 404 | Parcela não encontrada. |
| <a id="pagamento.parcela_nao_paga"></a>`pagamento.parcela_nao_paga` | 409 | Esta parcela não tem pagamento a estornar. |
| <a id="pagamento.parcela_paga"></a>`pagamento.parcela_paga` | 409 | Essa parcela já está paga. Se pagou de novo, fale com a tesouraria. |
| <a id="pagamento.parcelas_do_informe"></a>`pagamento.parcelas_do_informe` | 400 | Escolha de 1 a … parcelas para este pagamento. |
| <a id="pagamento.recebimento_estornado"></a>`pagamento.recebimento_estornado` | 409 | Esta baixa foi estornada, e não há recibo de pagamento desfeito. |
| <a id="pagamento.recebimento_nao_encontrado"></a>`pagamento.recebimento_nao_encontrado` | 404 | Recebimento não encontrado. |
| <a id="pagamento.sem_comprovante"></a>`pagamento.sem_comprovante` | 404 | Este aviso de pagamento não tem comprovante. |
| <a id="pagamento.sem_conta"></a>`pagamento.sem_conta` | 409 | A comissão ainda está configurando a conta de recebimento da turma. Tente de novo em alguns dias. |

### perfil

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="perfil.cep_invalido"></a>`perfil.cep_invalido` | 400 | CEP inválido. Use os 8 dígitos, como 80000-000. |
| <a id="perfil.cpf_invalido"></a>`perfil.cpf_invalido` | 400 | CPF inválido. Confira os 11 dígitos. |
| <a id="perfil.data_de_nascimento_invalida"></a>`perfil.data_de_nascimento_invalida` | 400 | A data de nascimento precisa ser de … a … anos atrás. |
| <a id="perfil.foto_grande"></a>`perfil.foto_grande` | 400 | A foto excede o limite de … MB. |
| <a id="perfil.foto_ilegivel"></a>`perfil.foto_ilegivel` | 400 | Não foi possível ler esta imagem. Envie outra foto. |
| <a id="perfil.foto_tipo_invalido"></a>`perfil.foto_tipo_invalido` | 400 | Envie uma imagem JPEG, PNG ou WebP. |
| <a id="perfil.foto_vazia"></a>`perfil.foto_vazia` | 400 | Nenhuma imagem foi enviada. |
| <a id="perfil.sem_foto"></a>`perfil.sem_foto` | 404 | Este formando ainda não enviou foto. |
| <a id="perfil.telefone_invalido"></a>`perfil.telefone_invalido` | 400 | Telefone inválido. Use DDD e número, como (41) 99876-5432, ou o formato internacional +55…. |
| <a id="perfil.uf_invalida"></a>`perfil.uf_invalida` | 400 | UF inválida. Use a sigla, como PR. |

### plano

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="plano.limite_de_formandos"></a>`plano.limite_de_formandos` | 409 | O plano … comporta … pessoas e a turma já tem …. Troque de plano para incluir mais gente. |

### privacidade

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="privacidade.anonimizacao_recusada"></a>`privacidade.anonimizacao_recusada` | 409 | Não foi possível anonimizar a conta. Tente de novo. |
| <a id="privacidade.nada_a_confirmar"></a>`privacidade.nada_a_confirmar` | 409 | Só a solicitação de exclusão precisa de confirmação. |
| <a id="privacidade.senha_invalida"></a>`privacidade.senha_invalida` | 401 | Senha incorreta. |
| <a id="privacidade.senha_obrigatoria"></a>`privacidade.senha_obrigatoria` | 400 | Digite sua senha para confirmar. |
| <a id="privacidade.solicitacao_encerrada"></a>`privacidade.solicitacao_encerrada` | 409 | Esta solicitação já foi atendida ou cancelada. |
| <a id="privacidade.solicitacao_nao_encontrada"></a>`privacidade.solicitacao_nao_encontrada` | 404 | Solicitação não encontrada. |
| <a id="privacidade.titular_nao_encontrado"></a>`privacidade.titular_nao_encontrado` | 404 | Titular não encontrado. |

### recebimento

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="recebimento.chave_pix_obrigatoria"></a>`recebimento.chave_pix_obrigatoria` | 409 | Com o Mercado Pago conectado, a chave PIX continua na conta: é por ela que o formando paga quando o Mercado Pago não responde. |
| <a id="recebimento.conta_ja_conferida"></a>`recebimento.conta_ja_conferida` | 409 | Esta chave já foi conferida. |
| <a id="recebimento.conta_sem_mudanca"></a>`recebimento.conta_sem_mudanca` | 409 | Estes dados são os mesmos da conta atual. |
| <a id="recebimento.provedor_recusou"></a>`recebimento.provedor_recusou` | 409 | O Mercado Pago recusou este pagamento. Tente outro meio ou fale com a comissão. |
| <a id="recebimento.sem_chave_pix"></a>`recebimento.sem_chave_pix` | 409 | Esta turma não aceita PIX. |
| <a id="recebimento.sem_conta"></a>`recebimento.sem_conta` | 404 | A turma ainda não cadastrou a conta de recebimento. |
| <a id="recebimento.sem_meio"></a>`recebimento.sem_meio` | 400 | Escolha ao menos um meio de recebimento para a turma. |

### relatorio

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="relatorio.nao_disponivel"></a>`relatorio.nao_disponivel` | 404 | Este relatório não está disponível para download. |

### suporte

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="suporte.assinatura_inexistente"></a>`suporte.assinatura_inexistente` | 409 | Esta turma nunca contratou um plano. Peça à comissão que escolha um na tela de planos. |
| <a id="suporte.conta_nao_encontrada"></a>`suporte.conta_nao_encontrada` | 404 | Conta não encontrada. |
| <a id="suporte.turma_nao_encontrada"></a>`suporte.turma_nao_encontrada` | 404 | Turma não encontrada. |

### usuario

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="usuario.autodesativacao"></a>`usuario.autodesativacao` | 409 | Você não pode desativar o próprio acesso. |
| <a id="usuario.autorrebaixamento"></a>`usuario.autorrebaixamento` | 409 | Você não pode remover o próprio perfil de administrador. |
| <a id="usuario.email_em_uso"></a>`usuario.email_em_uso` | 409 | Já existe uma conta com este e-mail. |
| <a id="usuario.nao_encontrado"></a>`usuario.nao_encontrado` | 404 | Usuário não encontrado. |
| <a id="usuario.perfil_desconhecido"></a>`usuario.perfil_desconhecido` | 400 | Perfil não reconhecido: {string.Join( |
| <a id="usuario.ultimo_administrador"></a>`usuario.ultimo_administrador` | 409 | Este é o único administrador ativo. Promova outro usuário antes de remover o acesso deste. |

### webhook

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="webhook.assinatura_invalida"></a>`webhook.assinatura_invalida` | 401 | Assinatura do webhook inválida. |
| <a id="webhook.payload_invalido"></a>`webhook.payload_invalido` | 400 | Corpo do webhook ilegível. |

---

Esta tabela é gerada a partir das chamadas a `Erro.*` no código-fonte. Código novo entra aqui na
mesma alteração que o cria — um `codigo` sem linha nesta tabela é um `type` apontando para uma
âncora que não existe.
