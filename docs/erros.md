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
| <a id="adesao.aditivo_desatualizado"></a>`adesao.aditivo_desatualizado` | 409 | O catálogo ou a cesta mudou entre a prévia e o aceite do aditivo. Peça a prévia de novo (Sprint 48, D38). |
| <a id="adesao.aditivo_sem_adesao"></a>`adesao.aditivo_sem_adesao` | 409 | Aditivo é mudança de uma adesão: o formando ainda não aderiu ao termo. |
| <a id="adesao.aditivo_sem_pacote"></a>`adesao.aditivo_sem_pacote` | 400 | O aditivo veio sem pacote. Escolha ao menos um para acrescentar. |
| <a id="adesao.aditivo_so_acrescenta"></a>`adesao.aditivo_so_acrescenta` | 409 | O pacote escolhido não custa mais que a faixa que ele substituiria: o aditivo só acrescenta (D38). Descer de faixa ou tirar um pacote é solicitação de cancelamento. |
| <a id="adesao.cadastro_incompleto"></a>`adesao.cadastro_incompleto` | 409 | Para aderir, informe no seu cadastro o nome completo, o CPF e a data de nascimento. |
| <a id="adesao.cesta_sem_escolha"></a>`adesao.cesta_sem_escolha` | 400 | Escolha ao menos um pacote para aderir (Sprint 47, D33). |
| <a id="adesao.codigo_invalido"></a>`adesao.codigo_invalido` | 409 | O código não confere ou já expirou. Peça um código novo e use o mais recente que chegou no seu e-mail. |
| <a id="adesao.cpf_em_uso"></a>`adesao.cpf_em_uso` | 409 | Este CPF já está na adesão de outra pessoa da turma. Confira o seu cadastro ou fale com a comissão. |
| <a id="adesao.ja_aderiu"></a>`adesao.ja_aderiu` | 409 | Você já aderiu a esta versão do termo. |
| <a id="adesao.menor_de_idade"></a>`adesao.menor_de_idade` | 409 | Quem tem menos de 18 anos adere com a comissão, junto com o responsável legal, e não pela plataforma. |
| <a id="adesao.nao_encontrada"></a>`adesao.nao_encontrada` | 404 | Adesão não encontrada. |
| <a id="adesao.pacote_ja_contratado"></a>`adesao.pacote_ja_contratado` | 400 | O pacote escolhido no aditivo já está na cesta do formando. |
| <a id="adesao.pendente"></a>`adesao.pendente` | 403 | O formando ainda não aderiu ao termo publicado da turma: só Meu termo e Meu cadastro respondem até o aceite (Sprint 47, D18). |
| <a id="adesao.resumo_descartado"></a>`adesao.resumo_descartado` | 400 | O modelo *{modelo}* devolveu *{n}* caracteres. Só no job do worker que gera o resumo do termo por IA; não chega à API. |
| <a id="adesao.sem_plano_vigente"></a>`adesao.sem_plano_vigente` | 409 | A turma ainda não tem plano de cobrança em vigor. |
| <a id="adesao.sem_termo_publicado"></a>`adesao.sem_termo_publicado` | 409 | A comissão ainda não publicou o termo de adesão da turma. |
| <a id="adesao.termo_desatualizado"></a>`adesao.termo_desatualizado` | 409 | O termo ou o plano mudou enquanto você lia. Confira a versão atual antes de aceitar. |
| <a id="adesao.termo_nao_encontrado"></a>`adesao.termo_nao_encontrado` | 404 | Versão do termo não encontrada. |
| <a id="adesao.termo_sem_mudanca"></a>`adesao.termo_sem_mudanca` | 409 | Este texto é igual ao da versão vigente. |

### agenda

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="agenda.evento_com_vendas"></a>`agenda.evento_com_vendas` | 409 | A festa tem compra da loja de pé ou pedido de convite de formando confirmado: não se marca "Cancelado" nem se exclui. <code>dados</code> traz <code>compras_da_loja</code> e <code>pedidos_de_convite</code> (Sprint 38, P10). |
| <a id="agenda.evento_nao_encontrado"></a>`agenda.evento_nao_encontrado` | 404 | Evento não encontrado na agenda. |
| <a id="agenda.tipo_unico"></a>`agenda.tipo_unico` | 409 | Esta turma já tem uma colação (ou uma festa) na agenda. Altere a data da que existe. |

### analytics

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="analytics.meses_invalidos"></a>`analytics.meses_invalidos` | 400 | Peça de 1 a 24 meses. |
| <a id="analytics.periodo_invertido"></a>`analytics.periodo_invertido` | 400 | O início do período precisa vir antes do fim. |
| <a id="analytics.periodo_longo"></a>`analytics.periodo_longo` | 400 | Escolha um período de até 92 dias. Para prazos mais longos, use a série mensal. |

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
| <a id="assinatura.cobranca_ja_paga"></a>`assinatura.cobranca_ja_paga` | 409 | Esta cobrança já foi paga. |
| <a id="assinatura.ja_ativa"></a>`assinatura.ja_ativa` | 409 | Esta formatura já tem uma assinatura ativa. |
| <a id="assinatura.mesmo_meio"></a>`assinatura.mesmo_meio` | 409 | A assinatura já é paga por este meio. |
| <a id="assinatura.nao_ativa"></a>`assinatura.nao_ativa` | 409 | Só uma assinatura ativa pode ser cancelada. |
| <a id="assinatura.nao_encontrada"></a>`assinatura.nao_encontrada` | 404 | Esta formatura ainda não contratou um plano. |
| <a id="assinatura.plano_invalido"></a>`assinatura.plano_invalido` | 400 | Plano não encontrado. |
| <a id="assinatura.plano_menor_que_a_turma"></a>`assinatura.plano_menor_que_a_turma` | 409 | A turma já tem … pessoas e o plano … comporta …. Escolha um plano maior. |
| <a id="assinatura.recorrencia_antiga"></a>`assinatura.recorrencia_antiga` | 409 | O aviso é de uma recorrência que a assinatura não usa mais. |
| <a id="assinatura.renovacao_ainda_nao_aberta"></a>`assinatura.renovacao_ainda_nao_aberta` | 409 | O PIX da renovação fica disponível a partir de *{dd/mm/aaaa}*, sete dias antes do vencimento. |
| <a id="assinatura.renovacao_automatica"></a>`assinatura.renovacao_automatica` | 409 | A renovação no cartão é automática: não há PIX a pagar. |
| <a id="assinatura.transicao_invalida"></a>`assinatura.transicao_invalida` | 409 | Uma assinatura … não pode passar para …. |
| <a id="assinatura.troca_de_ciclo"></a>`assinatura.troca_de_ciclo` | 409 | A troca de plano é no mesmo ciclo. Para mudar entre mensal e anual, cancele a renovação e contrate o outro ciclo quando a vigência acabar. |

### auth

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="auth.conta_bloqueada"></a>`auth.conta_bloqueada` | 403 | Conta temporariamente bloqueada por excesso de tentativas. Tente mais tarde. Só na troca de senha, depois de errar a senha atual vezes demais; no login, o bloqueio responde `auth.credenciais_invalidas`, igual à senha errada, de propósito. |
| <a id="auth.conta_desativada"></a>`auth.conta_desativada` | 403 | Esta conta está desativada. Procure um administrador. |
| <a id="auth.credenciais_invalidas"></a>`auth.credenciais_invalidas` | 401 | E-mail ou senha incorretos. |
| <a id="auth.email_nao_confirmado"></a>`auth.email_nao_confirmado` | 403 | Confirme seu e-mail antes de entrar. Verifique sua caixa de entrada. |
| <a id="auth.nao_autenticado"></a>`auth.nao_autenticado` | 401 | Autenticação necessária. Sem token, ou token vencido ou inválido. |
| <a id="auth.sem_permissao"></a>`auth.sem_permissao` | 403 | Você não tem permissão para esta operação. |
| <a id="auth.sessao_invalida"></a>`auth.sessao_invalida` | 401 | Sessão expirada. Faça login novamente. |

### cobranca

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="cobranca.alvo_invalido"></a>`cobranca.alvo_invalido` | 400 | Um dos pacotes do alvo do rateio não está no catálogo da turma (Sprint 48, D19). |
| <a id="cobranca.antecedencia_obrigatoria"></a>`cobranca.antecedencia_obrigatoria` | 400 | Diga com quantos dias de antecedência o desconto vale — senão ele sai para quem pagar um dia antes. |
| <a id="cobranca.cancelamento_fora_do_prazo"></a>`cobranca.cancelamento_fora_do_prazo` | 409 | Passou o "cancelável até" do item: o formando não abre mais solicitação de cancelamento (Sprint 48, D36). |
| <a id="cobranca.cesta_duplicada"></a>`cobranca.cesta_duplicada` | 400 | O mesmo pacote foi escolhido duas vezes. |
| <a id="cobranca.credito_acima_do_pago"></a>`cobranca.credito_acima_do_pago` | 400 | O crédito não pode passar do que o formando já pagou neste pedido. |
| <a id="cobranca.credito_da_tesouraria"></a>`cobranca.credito_da_tesouraria` | 403 | Só a tesouraria lança crédito de um pedido já pago. |
| <a id="cobranca.credito_invalido"></a>`cobranca.credito_invalido` | 400 | O crédito não pode ser negativo. |
| <a id="cobranca.dia_invalido"></a>`cobranca.dia_invalido` | 400 | O vencimento vai do dia 1 ao 31. |
| <a id="cobranca.estoque_esgotado"></a>`cobranca.estoque_esgotado` | 409 | Não há mais unidades deste item disponíveis. |
| <a id="cobranca.estoque_menor_que_reservado"></a>`cobranca.estoque_menor_que_reservado` | 409 | Já foram pedidas … unidades deste item. Cancele pedidos antes de reduzir o estoque. |
| <a id="cobranca.faixa_invalida"></a>`cobranca.faixa_invalida` | 400 | Duas faixas do mesmo grupo na cesta ("Festa 10" e "Festa 15"): escolha só uma (Sprint 47, D32). |
| <a id="cobranca.formando_nao_encontrado"></a>`cobranca.formando_nao_encontrado` | 404 | O formando do lançamento avulso não é membro ativo desta turma. |
| <a id="cobranca.item_com_pedido"></a>`cobranca.item_com_pedido` | 409 | Este item já foi pedido por alguém e não pode ser excluído. Encerre-o: ele para de aceitar pedidos e o que já foi pedido fica. |
| <a id="cobranca.item_da_festa_invalido"></a>`cobranca.item_da_festa_invalido` | 400 | Escolha um item da festa que exista, esteja de pé e seja rateado por formando. |
| <a id="cobranca.item_da_loja"></a>`cobranca.item_da_loja` | 409 | Este item é vendido pela loja da turma. Compre pelo link da loja (Sprint 26, P8). |
| <a id="cobranca.item_em_uso"></a>`cobranca.item_em_uso` | 409 | Este item já gerou parcelas: só o preço e a descrição podem mudar. Para mudar o resto, encerre-o e cadastre outro. |
| <a id="cobranca.item_encerrado"></a>`cobranca.item_encerrado` | 409 | Este item não está mais à venda. |
| <a id="cobranca.item_nao_e_opcional"></a>`cobranca.item_nao_e_opcional` | 400 | Este item é do plano da turma e não se pede — ele já está no seu extrato. |
| <a id="cobranca.item_nao_encontrado"></a>`cobranca.item_nao_encontrado` | 404 | Item opcional não encontrado. |
| <a id="cobranca.lancamento_retroativo"></a>`cobranca.lancamento_retroativo` | 400 | O primeiro vencimento do lançamento avulso está no passado: a parcela nasceria vencida (Sprint 48, D23). |
| <a id="cobranca.lancamento_sem_adesao"></a>`cobranca.lancamento_sem_adesao` | 409 | O formando ainda não aderiu ao termo: sem a adesão, não há regra de atraso para o lançamento avulso. |
| <a id="cobranca.limite_do_item_excedido"></a>`cobranca.limite_do_item_excedido` | 409 | Cada formando pode pedir no máximo … unidades deste item. |
| <a id="cobranca.motivo_da_recusa"></a>`cobranca.motivo_da_recusa` | 400 | Recusar uma solicitação de cancelamento exige o motivo, em até 300 caracteres — o formando o lê. |
| <a id="cobranca.motivo_longo"></a>`cobranca.motivo_longo` | 400 | O motivo da solicitação de cancelamento passou de 300 caracteres. |
| <a id="cobranca.nada_a_cancelar"></a>`cobranca.nada_a_cancelar` | 404 | O item não está na cesta nem nos pedidos confirmados do formando: rateio e lançamento avulso não se cancelam por solicitação. |
| <a id="cobranca.origem_obrigatoria"></a>`cobranca.origem_obrigatoria` | 400 | Informe onde a turma decidiu esta cobrança — a assembleia e a data. |
| <a id="cobranca.pacote_invalido"></a>`cobranca.pacote_invalido` | 400 | Um dos pacotes escolhidos não está mais no catálogo da turma. |
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
| <a id="cobranca.solicitacao_ja_respondida"></a>`cobranca.solicitacao_ja_respondida` | 409 | A solicitação de cancelamento já foi aprovada ou recusada. |
| <a id="cobranca.solicitacao_nao_encontrada"></a>`cobranca.solicitacao_nao_encontrada` | 404 | Solicitação de cancelamento não encontrada nesta turma. |
| <a id="cobranca.tipo_invalido"></a>`cobranca.tipo_invalido` | 400 | Este tipo de cobrança não cabe aqui. |
| <a id="cobranca.ultima_parcela_depois_do_limite"></a>`cobranca.ultima_parcela_depois_do_limite` | 400 | A última parcela venceria depois do último vencimento que a comissão definiu para o item (Sprint 47, D28) — no cadastro do pacote ou do opcional, e na divisão de cada pedido. |
| <a id="cobranca.valor_invalido"></a>`cobranca.valor_invalido` | 400 | Informe um valor maior que zero. |
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
| <a id="convite.formatura_nao_contratada"></a>`convite.formatura_nao_contratada` | 403 | Formandos entram só com um plano contratado em dia. Antes disso, dá para montar a comissão. |
| <a id="convite.invalido"></a>`convite.invalido` | 404 | Este convite não está mais disponível. Peça um novo à comissão. |
| <a id="convite.ja_vinculado"></a>`convite.ja_vinculado` | 409 | Você já participa desta formatura. |
| <a id="convite.link_so_para_formando"></a>`convite.link_so_para_formando` | 400 | O link da turma só convida formandos. Para a comissão, envie o convite por e-mail. |
| <a id="convite.membro_desligado"></a>`convite.membro_desligado` | 403 | Você foi desligado desta formatura e o desligamento é definitivo. Fale com a comissão. |
| <a id="convite.nao_encontrado"></a>`convite.nao_encontrado` | 404 | Convite não encontrado nesta formatura. |
| <a id="convite.papel_invalido"></a>`convite.papel_invalido` | 400 | Papel inválido. Use Presidente, Tesoureiro, Comissao ou Formando. |
| <a id="convite.papel_restrito"></a>`convite.papel_restrito` | 403 | Só o Presidente convida para a comissão e a tesouraria. |
| <a id="convite.sem_termo_ou_plano"></a>`convite.sem_termo_ou_plano` | 409 | Convite de formando antes de a turma publicar o termo e pôr o plano em vigor (Sprint 47, D34). |
| <a id="convite.vinculo_removido"></a>`convite.vinculo_removido` | 403 | Você foi removido desta formatura. Para voltar, peça à comissão um convite pessoal. |

### erro

Não vem do domínio: é o `GlobalExceptionHandler`, a rede de segurança para exceção não tratada. O status sai da exceção.

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="erro.inesperado"></a>`erro.inesperado` | 500 / 400 / 409 / 413 / 502 / 503 | Ocorreu um erro inesperado. Também cobre a corrida que o banco recusa (409 "Já existe um registro com estes dados.", 409 de concorrência), corpo ilegível (400), requisição grande demais (413), serviço externo fora do esperado (502) e banco fora do ar (503). O `trace_id` é o que resolve. |

### estorno

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="estorno.cobranca_nao_paga"></a>`estorno.cobranca_nao_paga` | 409 | Só um pagamento confirmado, e ainda não estornado, pode ser estornado. |
| <a id="estorno.fora_do_prazo"></a>`estorno.fora_do_prazo` | 409 | O reembolso integral vale até 7 dias depois do pagamento. Depois disso, só o proporcional, nos casos dos Termos. |
| <a id="estorno.nada_a_devolver"></a>`estorno.nada_a_devolver` | 409 | A vigência deste pagamento já acabou: não há o que devolver pelo proporcional. |

### festa

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="festa.convite_nao_encontrado"></a>`festa.convite_nao_encontrado` | 404 | Convite não encontrado. Genérico de propósito: código inexistente, assinatura adulterada, convite revogado, convite ainda "a definir" (sem convidado, não há ingresso) e convite de outra turma respondem igual na página pública. |
| <a id="festa.convite_preso"></a>`festa.convite_preso` | 409 | Convite de pacote preso: o formando tem parcela vencida além da carência. Volta a valer ao regularizar ou quando a comissão libera (Sprint 47, D24). |
| <a id="festa.convite_revogado"></a>`festa.convite_revogado` | 409 | O convite foi revogado (estorno, cancelamento, transferência ou reemissão); a mensagem traz o motivo. |
| <a id="festa.convite_sem_titular"></a>`festa.convite_sem_titular` | 409 | O convite ainda não tem nome e documento do convidado — não entra como anônimo (P5). |
| <a id="festa.disputa_encerrada"></a>`festa.disputa_encerrada` | 409 | Este item já foi contratado ou cancelado: a escolha da turma já aconteceu. |
| <a id="festa.documento_nao_encontrado"></a>`festa.documento_nao_encontrado` | 400 | Documento não encontrado no acervo, ou visível apenas para a comissão. |
| <a id="festa.documento_obrigatorio"></a>`festa.documento_obrigatorio` | 400 | Depois do fechamento da lista, o convite precisa de nome e documento. |
| <a id="festa.email_nao_confirmado"></a>`festa.email_nao_confirmado` | 403 | Confirme seu e-mail para votar. Use o link que enviamos quando você criou a conta, ou peça outro em Minha conta. |
| <a id="festa.entrada_nao_encontrada"></a>`festa.entrada_nao_encontrada` | 404 | Entrada da portaria não encontrada. |
| <a id="festa.evento_incompleto"></a>`festa.evento_incompleto` | 409 | A festa precisa estar na agenda com data, hora e local antes de qualquer convite sair (P6). |
| <a id="festa.evento_sem_convite"></a>`festa.evento_sem_convite` | 400 | Só a festa e a colação têm cota de convites. |
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
| <a id="financeiro.fornecedor_nao_encontrado"></a>`financeiro.fornecedor_nao_encontrado` | 400 / 404 | Fornecedor não encontrado. 404 ao abrir o fornecedor; 400, no campo `fornecedor_id`, ao lançar ou alterar uma despesa. |
| <a id="financeiro.fornecedor_nome_em_uso"></a>`financeiro.fornecedor_nome_em_uso` | 409 | A turma já tem um fornecedor com este nome. |
| <a id="financeiro.item_da_festa_nao_encontrado"></a>`financeiro.item_da_festa_nao_encontrado` | 400 | Item da festa não encontrado. |
| <a id="financeiro.outra_receita_cancelada"></a>`financeiro.outra_receita_cancelada` | 409 | Esta receita foi cancelada. Lance uma nova. |
| <a id="financeiro.outra_receita_data_futura"></a>`financeiro.outra_receita_data_futura` | 400 | Receita recebida não pode ter data no futuro. |
| <a id="financeiro.outra_receita_duplicada"></a>`financeiro.outra_receita_duplicada` | 409 | Esta receita já foi lançada para esta data. Confira a lista antes de lançar de novo. |
| <a id="financeiro.outra_receita_estorno"></a>`financeiro.outra_receita_estorno` | 409 | O lançamento é o estorno de uma venda da loja e não se edita (Sprint 38, P5). |
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
| <a id="formatura.nao_selecionada"></a>`formatura.nao_selecionada` | 403 | Nenhuma formatura selecionada. |
| <a id="formatura.pendencias_em_aberto"></a>`formatura.pendencias_em_aberto` | 409 | Há dinheiro pendente na turma — parcela, aviso de pagamento, pedido não quitado, compra da loja pendente ou a devolver, pedido de cancelamento ou cobrança viva. A mensagem lista o que falta, e <code>dados</code> traz as contagens (Sprint 38, P11). |
| <a id="formatura.sem_vinculo"></a>`formatura.sem_vinculo` | 403 | Você não participa desta formatura. |
| <a id="formatura.transicao_invalida"></a>`formatura.transicao_invalida` | 409 | Uma formatura … não pode passar para …. |
| <a id="formatura.ultimo_presidente"></a>`formatura.ultimo_presidente` | 409 | A formatura precisa de ao menos um presidente ativo. Promova outra pessoa antes. |
| <a id="formatura.vinculo_nao_encontrado"></a>`formatura.vinculo_nao_encontrado` | 404 | Você não é membro ativo desta turma. |

### ia

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="ia.desligada"></a>`ia.desligada` | 503 | A IA não está configurada. Só nos jobs do worker que chamam a IA; não chega à API. |
| <a id="ia.indisponivel"></a>`ia.indisponivel` | 503 | Nenhum modelo devolveu resposta. Só nos jobs do worker que chamam a IA; a próxima rodada tenta de novo. |

### identity

Erros do ASP.NET Identity no cadastro, na troca e na redefinição de senha. O código é o prefixo `identity.` seguido do código do Identity em snake_case (`PasswordTooShort` → `identity.password_too_short`), e a mensagem é a de `MensagensDeIdentity`. Todos saem como 400 de validação, no campo `senha` (cadastro), `nova_senha` ou `senha_atual` (troca e redefinição). A redefinição com token vencido responde `conta.link_invalido`, não `identity.invalid_token`.

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="identity.duplicate_email"></a>`identity.duplicate_email` | 400 | Já existe uma conta com este e-mail. |
| <a id="identity.duplicate_user_name"></a>`identity.duplicate_user_name` | 400 | Já existe uma conta com este e-mail. |
| <a id="identity.invalid_email"></a>`identity.invalid_email` | 400 | O e-mail informado não é válido. |
| <a id="identity.password_mismatch"></a>`identity.password_mismatch` | 400 | A senha atual está incorreta. Na troca de senha; conta para o bloqueio `auth.conta_bloqueada`. |
| <a id="identity.password_requires_digit"></a>`identity.password_requires_digit` | 400 | A senha deve conter ao menos um número. |
| <a id="identity.password_requires_lower"></a>`identity.password_requires_lower` | 400 | A senha deve conter ao menos uma letra minúscula. |
| <a id="identity.password_requires_upper"></a>`identity.password_requires_upper` | 400 | A senha deve conter ao menos uma letra maiúscula. |
| <a id="identity.password_too_short"></a>`identity.password_too_short` | 400 | A senha deve ter no mínimo *{n}* caracteres (hoje, 8). |

### legal

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="legal.aceite_obrigatorio"></a>`legal.aceite_obrigatorio` | 400 | É preciso aceitar os Termos de Uso e a Política de Privacidade para criar a conta. |
| <a id="legal.consentimento_ja_revogado"></a>`legal.consentimento_ja_revogado` | 409 | Este consentimento já foi revogado. |
| <a id="legal.consentimento_nao_encontrado"></a>`legal.consentimento_nao_encontrado` | 404 | Registro de consentimento não encontrado. |
| <a id="legal.documento_nao_encontrado"></a>`legal.documento_nao_encontrado` | 404 | Documento não encontrado. |
| <a id="legal.versao_desatualizada"></a>`legal.versao_desatualizada` | 409 | Este documento foi atualizado. Leia e aceite a versão vigente. |

### loja

A loja pública da Sprint 26. As rotas são anônimas: 404 vale para link errado, antigo ou inexistente.

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="loja.compra_ja_cancelada"></a>`loja.compra_ja_cancelada` | 409 | Os convites pedidos já foram cancelados — cancelar de novo não devolve lugar nem estorna outra vez (Sprint 38, decisão 3). |
| <a id="loja.compra_nao_a_devolver"></a>`loja.compra_nao_a_devolver` | 409 | A compra não está na lista a devolver. |
| <a id="loja.compra_nao_encontrada"></a>`loja.compra_nao_encontrada` | 404 | Não encontramos esta compra — o link é antigo, errado ou foi substituído por um reenvio. |
| <a id="loja.compra_nao_paga"></a>`loja.compra_nao_paga` | 409 | Compra pendente não se cancela: ela expira sozinha se o PIX não for pago (Sprint 38, P8). |
| <a id="loja.compra_nao_pendente"></a>`loja.compra_nao_pendente` | 409 | Esta compra não está esperando pagamento no cartão. Se a reserva venceu, faça uma compra nova. |
| <a id="loja.comprovante_invalido"></a>`loja.comprovante_invalido` | 400 | O comprovante da devolução precisa ser PDF ou imagem (PNG, JPG ou WebP). |
| <a id="loja.comprovante_obrigatorio"></a>`loja.comprovante_obrigatorio` | 400 | Marcar a compra devolvida exige o comprovante do PIX de volta (Sprint 38, decisão 2). |
| <a id="loja.convite_ja_validado"></a>`loja.convite_ja_validado` | 409 | O convite já entrou na festa, e convite usado não se cancela; a Gestão desfaz a entrada antes, se for o caso (Sprint 38, P3). |
| <a id="loja.cpf_invalido"></a>`loja.cpf_invalido` | 400 | CPF com dígito verificador inválido; recusado antes da reserva (P6). |
| <a id="loja.dados_ainda_necessarios"></a>`loja.dados_ainda_necessarios` | 409 | Os dados do comprador sustentam os convites e a devolução até a festa. |
| <a id="loja.esgotado"></a>`loja.esgotado` | 409 | Os convites esgotaram — ou restam menos que a quantidade pedida. Responde antes da fila (decisão 8). |
| <a id="loja.fila_cheia"></a>`loja.fila_cheia` | 429 | Muita gente comprando na mesma turma. Tente de novo depois do <code>Retry-After</code>, com a mesma chave de idempotência. |
| <a id="loja.item_nao_encontrado"></a>`loja.item_nao_encontrado` | 404 | Este convite não está à venda nesta loja. |
| <a id="loja.limite_por_pessoa"></a>`loja.limite_por_pessoa` | 409 | O CPF já tem o máximo de convites deste tipo (P3). |
| <a id="loja.nao_encontrada"></a>`loja.nao_encontrada` | 404 | A turma não existe, não está ativa, não vende nada pela loja ou o plano dela não inclui a festa. |
| <a id="loja.pedido_ja_respondido"></a>`loja.pedido_ja_respondido` | 409 | O pedido de cancelamento já foi aprovado ou recusado. |
| <a id="loja.pedido_nao_encontrado"></a>`loja.pedido_nao_encontrado` | 404 | Pedido de cancelamento não encontrado nesta turma. |
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
| <a id="pagamento.aviso_desligado"></a>`pagamento.aviso_desligado` | 409 | A turma cobra pelo Mercado Pago (29/09/2026): não há aviso de pagamento, e quem pagou por fora fala com a tesouraria. |
| <a id="pagamento.aviso_nao_autentico"></a>`pagamento.aviso_nao_autentico` | 401 | Assinatura inválida. O aviso (webhook) do Mercado Pago não passou na conferência da assinatura. |
| <a id="pagamento.cartao_desligado"></a>`pagamento.cartao_desligado` | 409 | A turma não está aceitando cartão agora. Faça a compra pelo PIX. |
| <a id="pagamento.cartao_recusado"></a>`pagamento.cartao_recusado` | 409 | O cartão foi recusado pelo emissor ou pelo Mercado Pago. Confira os dados, tente outro cartão ou pague pelo PIX (Sprint 35; a tela chega na 37). |
| <a id="pagamento.cobranca_em_emissao"></a>`pagamento.cobranca_em_emissao` | 409 | Este pagamento está sendo gerado em outra tela. Aguarde um instante e tente de novo. |
| <a id="pagamento.comprovante_invalido"></a>`pagamento.comprovante_invalido` | 400 | Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP). |
| <a id="pagamento.comprovante_obrigatorio"></a>`pagamento.comprovante_obrigatorio` | 400 | Anexe o comprovante do PIX de volta. |
| <a id="pagamento.devolucao_exige_comprovante"></a>`pagamento.devolucao_exige_comprovante` | 409 | Este valor voltou ao formando por PIX: registre a devolução com o comprovante. |
| <a id="pagamento.informe_ja_conferido"></a>`pagamento.informe_ja_conferido` | 409 | Este aviso de pagamento já foi conferido. |
| <a id="pagamento.informe_nao_encontrado"></a>`pagamento.informe_nao_encontrado` | 404 | Aviso de pagamento não encontrado. |
| <a id="pagamento.informe_pendente"></a>`pagamento.informe_pendente` | 409 | Você já avisou o pagamento de uma dessas parcelas. A tesouraria vai conferir, e você recebe um e-mail quando for confirmado. |
| <a id="pagamento.mercado_pago_indisponivel"></a>`pagamento.mercado_pago_indisponivel` | 503 | Na cobrança automática, o Mercado Pago não respondeu ao gerar o PIX. Tente de novo em alguns minutos. |
| <a id="pagamento.pago_sem_parcela_nao_devolve"></a>`pagamento.pago_sem_parcela_nao_devolve` | 409 | Este dinheiro está na conta do Mercado Pago, não no caixa: devolva pelo painel dele ou lance como outra receita, e feche o aviso. |
| <a id="pagamento.parcela_nao_aberta"></a>`pagamento.parcela_nao_aberta` | 409 | Esta parcela não está em aberto. |
| <a id="pagamento.parcela_nao_encontrada"></a>`pagamento.parcela_nao_encontrada` | 404 | Parcela não encontrada. |
| <a id="pagamento.parcela_nao_paga"></a>`pagamento.parcela_nao_paga` | 409 | Esta parcela não tem pagamento a estornar. |
| <a id="pagamento.parcela_paga"></a>`pagamento.parcela_paga` | 409 | Essa parcela já está paga. Se pagou de novo, fale com a tesouraria. |
| <a id="pagamento.parcelas_do_informe"></a>`pagamento.parcelas_do_informe` | 400 | Escolha de 1 a … parcelas para este pagamento. |
| <a id="pagamento.recebimento_estornado"></a>`pagamento.recebimento_estornado` | 409 | Esta baixa foi estornada, e não há recibo de pagamento desfeito. |
| <a id="pagamento.recebimento_nao_encontrado"></a>`pagamento.recebimento_nao_encontrado` | 404 | Recebimento não encontrado. |
| <a id="pagamento.sem_comprovante"></a>`pagamento.sem_comprovante` | 404 | Este aviso de pagamento não tem comprovante. |
| <a id="pagamento.sem_conta"></a>`pagamento.sem_conta` | 409 | A comissão ainda está configurando a conta de recebimento da turma. Tente de novo em alguns dias. |
| <a id="pagamento.valor_mudou"></a>`pagamento.valor_mudou` | 409 | O valor mudou para *{R$ novo valor}*. Confira e pague de novo — o cartão não foi cobrado. |
| <a id="pagamento.valor_nao_a_devolver"></a>`pagamento.valor_nao_a_devolver` | 409 | Este valor não está mais na lista a devolver. |
| <a id="pagamento.valor_nao_encontrado"></a>`pagamento.valor_nao_encontrado` | 404 | Valor a devolver não encontrado. |

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
| <a id="plano.modulo_nao_incluido"></a>`plano.modulo_nao_incluido` | 403 | Esta área não está incluída no plano da turma. |
| <a id="plano.nao_encontrado"></a>`plano.nao_encontrado` | 404 | O catálogo não tem o plano desta turma. |

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

### rate_limit

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="rate_limit.excedido"></a>`rate_limit.excedido` | 429 | Muitas requisições. Tente novamente em instantes. Vem com `Retry-After`, em segundos. |

### recebimento

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="recebimento.autorizacao_recusada"></a>`recebimento.autorizacao_recusada` | 409 | O Mercado Pago recusou a autorização. Conecte a conta da turma de novo. |
| <a id="recebimento.avisos_pendentes"></a>`recebimento.avisos_pendentes` | 409 | Trocar para a cobrança automática com avisos de pagamento esperando conferência. |
| <a id="recebimento.chave_pix_obrigatoria"></a>`recebimento.chave_pix_obrigatoria` | 409 | Com o Mercado Pago conectado, a chave PIX continua na conta: é por ela que o formando paga quando o Mercado Pago não responde. |
| <a id="recebimento.cobranca_automatica_ligada"></a>`recebimento.cobranca_automatica_ligada` | 409 | Desconectar o Mercado Pago com a turma na cobrança automática: volte ao manual antes. |
| <a id="recebimento.conta_fora_do_brasil"></a>`recebimento.conta_fora_do_brasil` | 409 | Esta conta do Mercado Pago não é do Brasil. Conecte a conta brasileira da turma. |
| <a id="recebimento.conta_ja_conferida"></a>`recebimento.conta_ja_conferida` | 409 | Esta chave já foi conferida. |
| <a id="recebimento.conta_sem_mudanca"></a>`recebimento.conta_sem_mudanca` | 409 | Estes dados são os mesmos da conta atual. |
| <a id="recebimento.loja_aberta"></a>`recebimento.loja_aberta` | 409 | Desconectar o Mercado Pago com item à venda na loja pública: ela só vende por ele. Encerre as vendas antes. |
| <a id="recebimento.pix_em_aberto"></a>`recebimento.pix_em_aberto` | 409 | Voltar à cobrança manual com PIX de parcela do Mercado Pago ainda pagável (vale até a meia-noite). |
| <a id="recebimento.provedor_desligado"></a>`recebimento.provedor_desligado` | 409 | A conexão com o Mercado Pago ainda não está disponível. |
| <a id="recebimento.provedor_indisponivel"></a>`recebimento.provedor_indisponivel` | 503 | O Mercado Pago não respondeu agora. Tente de novo em alguns minutos. |
| <a id="recebimento.provedor_nao_conectado"></a>`recebimento.provedor_nao_conectado` | 404 | A turma não tem o Mercado Pago conectado. |
| <a id="recebimento.provedor_recusou"></a>`recebimento.provedor_recusou` | 409 | O Mercado Pago recusou este pagamento. Tente outro meio ou fale com a comissão. |
| <a id="recebimento.reconectar_para_cartao"></a>`recebimento.reconectar_para_cartao` | 409 | Esta conexão é anterior ao cartão. Clique em Trocar de conta e autorize a mesma conta de novo para ligar o cartão. |
| <a id="recebimento.retorno_invalido"></a>`recebimento.retorno_invalido` | 400 | Este link de autorização venceu ou não é válido. Volte ao Kapa e clique em Conectar de novo. |
| <a id="recebimento.sem_chave_pix"></a>`recebimento.sem_chave_pix` | 409 | Esta turma não aceita PIX. |
| <a id="recebimento.sem_conta"></a>`recebimento.sem_conta` | 404 | A turma ainda não cadastrou a conta de recebimento. |
| <a id="recebimento.sem_meio"></a>`recebimento.sem_meio` | 400 | Escolha ao menos um meio de recebimento para a turma. |
| <a id="recebimento.somente_presidente"></a>`recebimento.somente_presidente` | 403 | Só o Presidente conecta o Mercado Pago da turma. |

### relatorio

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="relatorio.nao_disponivel"></a>`relatorio.nao_disponivel` | 404 | Este relatório não está disponível para download. |

### suporte

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="suporte.assinatura_inexistente"></a>`suporte.assinatura_inexistente` | 409 | Esta turma nunca contratou um plano. Peça à comissão que escolha um na tela de planos. |
| <a id="suporte.conta_nao_encontrada"></a>`suporte.conta_nao_encontrada` | 404 | Conta não encontrada. |
| <a id="suporte.mes_invalido"></a>`suporte.mes_invalido` | 400 | Escolha um mês válido. |
| <a id="suporte.pagamento_nao_encontrado"></a>`suporte.pagamento_nao_encontrado` | 404 | Pagamento não encontrado nesta turma. |
| <a id="suporte.turma_nao_encontrada"></a>`suporte.turma_nao_encontrada` | 404 | Turma não encontrada. |

### usuario

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="usuario.autodesativacao"></a>`usuario.autodesativacao` | 409 | Você não pode desativar o próprio acesso. |
| <a id="usuario.autorrebaixamento"></a>`usuario.autorrebaixamento` | 409 | Você não pode remover o próprio perfil de administrador. |
| <a id="usuario.email_em_uso"></a>`usuario.email_em_uso` | 409 | Já existe uma conta com este e-mail. |
| <a id="usuario.nao_encontrado"></a>`usuario.nao_encontrado` | 404 | Usuário não encontrado. |
| <a id="usuario.perfil_desconhecido"></a>`usuario.perfil_desconhecido` | 400 | Perfil não reconhecido: *{perfis enviados que não existem, separados por vírgula}*. |
| <a id="usuario.ultimo_administrador"></a>`usuario.ultimo_administrador` | 409 | Este é o único administrador ativo. Promova outro usuário antes de remover o acesso deste. |

### validacao

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="validacao.invalido"></a>`validacao.invalido` | 400 | O corpo não passou na validação de forma. Cada regra sem código próprio sai com este; as mensagens vêm por campo em `errors`. |

### webhook

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="webhook.assinatura_invalida"></a>`webhook.assinatura_invalida` | 401 | Assinatura do webhook inválida. |
| <a id="webhook.payload_invalido"></a>`webhook.payload_invalido` | 400 | Corpo do webhook ilegível. |

---

Esta tabela é escrita à mão. Código novo entra aqui na mesma alteração que o cria — um `codigo`
sem linha nesta tabela é um `type` apontando para uma âncora que não existe. O teste
`CatalogoDeErrosTests` (em `tests/Backend.UnitTests/Documentacao`) varre `src/` e falha quando um
código emitido não tem âncora aqui, ou quando uma âncora aponta para um código que nada mais emite.
