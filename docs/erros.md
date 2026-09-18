| <a id="membro.nao_encontrado"></a>`membro.nao_encontrado` | undefined | Membro não encontrado nesta formatura. || <a id="formatura.sem_vinculo"></a>`formatura.sem_vinculo` | undefined | Você não participa desta formatura. || <a id="formatura.inativa"></a>`formatura.inativa` | undefined | Esta formatura está em modo leitura e não aceita alterações. || <a id="legal.consentimento_nao_encontrado"></a>`legal.consentimento_nao_encontrado` | 404 | Registro de consentimento não encontrado. |
| <a id="legal.consentimento_ja_revogado"></a>`legal.consentimento_ja_revogado` | 409 | Este consentimento já foi revogado. |
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

Fora dos seis, um **429** com `codigo` `rate_limit.excedido` e o cabeçalho `Retry-After` em
segundos, e um **500** com `codigo` `erro.inesperado` — este último é sempre bug ou indisponibilidade
nossa, e o `trace_id` é o que o resolve.

## Códigos por recurso

### adesao

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="adesao.cadastro_incompleto"></a>`adesao.cadastro_incompleto` | 409 | Para aderir, informe no seu cadastro o nome completo, o CPF e a data de nascimento. |
| <a id="adesao.ja_aderiu"></a>`adesao.ja_aderiu` | 409 | Este membro já aderiu à versão vigente do termo. |
| <a id="adesao.nao_encontrada"></a>`adesao.nao_encontrada` | 404 | Adesão não encontrada. |
| <a id="adesao.sem_plano_vigente"></a>`adesao.sem_plano_vigente` | 409 | A turma ainda não tem plano de cobrança em vigor. |
| <a id="adesao.sem_termo_publicado"></a>`adesao.sem_termo_publicado` | 409 | A comissão ainda não publicou o termo de adesão da turma. |
| <a id="adesao.termo_sem_mudanca"></a>`adesao.termo_sem_mudanca` | 409 | Este texto é igual ao da versão vigente. |

### arquivo

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="arquivo.conteudo_indisponivel"></a>`arquivo.conteudo_indisponivel` | 404 | O conteúdo deste arquivo não está disponível. |
| <a id="arquivo.nao_encontrado"></a>`arquivo.nao_encontrado` | 404 | Arquivo não encontrado. |

### assinatura

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="assinatura.ja_ativa"></a>`assinatura.ja_ativa` | 409 | Esta formatura já tem uma assinatura ativa. |
| <a id="assinatura.nao_ativa"></a>`assinatura.nao_ativa` | 409 | Só uma assinatura ativa pode ser cancelada. |
| <a id="assinatura.nao_encontrada"></a>`assinatura.nao_encontrada` | 404 | Esta formatura ainda não contratou um plano. |
| <a id="assinatura.plano_invalido"></a>`assinatura.plano_invalido` | 400 | Plano não encontrado. |

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
| <a id="cobranca.item_encerrado"></a>`cobranca.item_encerrado` | 409 | Este item foi encerrado e não muda mais. |
| <a id="cobranca.item_nao_encontrado"></a>`cobranca.item_nao_encontrado` | 404 | Item não encontrado neste plano. |
| <a id="cobranca.plano_ja_vigente"></a>`cobranca.plano_ja_vigente` | 409 | Este plano já está em vigor. |
| <a id="cobranca.plano_nao_encontrado"></a>`cobranca.plano_nao_encontrado` | 404 | Plano de cobrança não encontrado. |
| <a id="cobranca.plano_sem_itens"></a>`cobranca.plano_sem_itens` | 409 | Inclua ao menos um item antes de colocar o plano em vigor. |
| <a id="cobranca.plano_vigente_existente"></a>`cobranca.plano_vigente_existente` | 409 | A turma já tem um plano em vigor. Só um vale por vez. |
| <a id="cobranca.sem_plano_vigente"></a>`cobranca.sem_plano_vigente` | 409 | A turma ainda não tem plano de cobrança em vigor. |

### comunicacao

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="comunicacao.arquivo_obrigatorio"></a>`comunicacao.arquivo_obrigatorio` | 400 | Anexe o arquivo do documento. |
| <a id="comunicacao.aviso_nao_encontrado"></a>`comunicacao.aviso_nao_encontrado` | 404 | Aviso não encontrado. |
| <a id="comunicacao.documento_nao_encontrado"></a>`comunicacao.documento_nao_encontrado` | 404 | Documento não encontrado. |

### conta

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="conta.link_invalido"></a>`conta.link_invalido` | 400 | Este link é inválido ou expirou. Solicite um novo. |

### convite

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="convite.esgotado"></a>`convite.esgotado` | 409 | Este convite acabou de atingir o limite de entradas. Peça um novo à comissão. |
| <a id="convite.invalido"></a>`convite.invalido` | 404 | Este convite não está mais disponível. Peça um novo à comissão. |
| <a id="convite.ja_vinculado"></a>`convite.ja_vinculado` | 409 | Você já participa desta formatura. |
| <a id="convite.nao_encontrado"></a>`convite.nao_encontrado` | 404 | Convite não encontrado nesta formatura. |
| <a id="convite.papel_restrito"></a>`convite.papel_restrito` | 403 | Só o Presidente convida para a comissão e a tesouraria. |

### financeiro

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="financeiro.comprovante_invalido"></a>`financeiro.comprovante_invalido` | 400 | Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP). |
| <a id="financeiro.despesa_cancelada"></a>`financeiro.despesa_cancelada` | 409 | Esta despesa foi cancelada. Lance uma nova. |
| <a id="financeiro.despesa_nao_encontrada"></a>`financeiro.despesa_nao_encontrada` | 404 | Despesa não encontrada. |
| <a id="financeiro.despesa_nao_prevista"></a>`financeiro.despesa_nao_prevista` | 409 | Esta despesa não está prevista para pagamento. |
| <a id="financeiro.fornecedor_em_uso"></a>`financeiro.fornecedor_em_uso` | 409 | Este fornecedor já tem despesa lançada. Desative o cadastro em vez de excluí-lo. |
| <a id="financeiro.fornecedor_nao_encontrado"></a>`financeiro.fornecedor_nao_encontrado` | 400 | Fornecedor não encontrado. |
| <a id="financeiro.fornecedor_nome_em_uso"></a>`financeiro.fornecedor_nome_em_uso` | 409 | A turma já tem um fornecedor com este nome. |
| <a id="financeiro.sem_comprovante"></a>`financeiro.sem_comprovante` | 404 | Esta despesa não tem comprovante. |

### formando

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="formando.nao_encontrado"></a>`formando.nao_encontrado` | 404 | Formando não encontrado nesta formatura. |

### formatura

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="formatura.assinatura_ativa"></a>`formatura.assinatura_ativa` | 409 | Cancele a renovação da assinatura antes de encerrar a formatura. |
| <a id="formatura.encerrada"></a>`formatura.encerrada` | 409 | Uma formatura encerrada não contrata assinatura. |
| <a id="formatura.inativa"></a>`formatura.inativa` | 403 | Esta formatura está em modo leitura e não aceita alterações. |
| <a id="formatura.membro_ja_desligado"></a>`formatura.membro_ja_desligado` | 409 | Esta pessoa já foi desligada da turma. |
| <a id="formatura.membro_nao_desligado"></a>`formatura.membro_nao_desligado` | 409 | Esta pessoa não está desligada da turma. |
| <a id="formatura.membro_sem_adesao"></a>`formatura.membro_sem_adesao` | 409 | Esta pessoa ainda não aderiu ao termo e não deve nada à turma. Use Remover. |
| <a id="formatura.nao_encontrada"></a>`formatura.nao_encontrada` | 404 | Formatura não encontrada. |
| <a id="formatura.sem_vinculo"></a>`formatura.sem_vinculo` | 403 | Você não participa desta formatura. |
| <a id="formatura.ultimo_presidente"></a>`formatura.ultimo_presidente` | 409 | A formatura precisa de ao menos um presidente ativo. Promova outra pessoa antes. |

### legal

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="legal.documento_nao_encontrado"></a>`legal.documento_nao_encontrado` | 404 | Documento não encontrado. |
| <a id="legal.versao_desatualizada"></a>`legal.versao_desatualizada` | 409 | Este documento foi atualizado. Leia e aceite a versão vigente. |

### membro

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="membro.detalhe_obrigatorio"></a>`membro.detalhe_obrigatorio` | 400 | Diga qual foi o motivo. (Só com o motivo `Outro`.) |
| <a id="membro.motivo_invalido"></a>`membro.motivo_invalido` | 400 | Motivo inválido. Escolha um da lista. |
| <a id="membro.nao_encontrado"></a>`membro.nao_encontrado` | 404 | Membro não encontrado nesta formatura. |
| <a id="membro.papel_invalido"></a>`membro.papel_invalido` | 400 | Papel inválido. Use Presidente, Tesoureiro, Comissao ou Formando. |

### pagamento

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="pagamento.comprovante_invalido"></a>`pagamento.comprovante_invalido` | 400 | Envie o comprovante em PDF ou imagem (PNG, JPG ou WebP). |
| <a id="pagamento.informe_ja_conferido"></a>`pagamento.informe_ja_conferido` | 409 | Este aviso de pagamento já foi conferido. |
| <a id="pagamento.informe_nao_encontrado"></a>`pagamento.informe_nao_encontrado` | 404 | Aviso de pagamento não encontrado. |
| <a id="pagamento.parcela_nao_aberta"></a>`pagamento.parcela_nao_aberta` | 409 | Esta parcela não está em aberto. |
| <a id="pagamento.parcela_nao_encontrada"></a>`pagamento.parcela_nao_encontrada` | 404 | Parcela não encontrada. |
| <a id="pagamento.parcela_nao_paga"></a>`pagamento.parcela_nao_paga` | 409 | Esta parcela não está paga. |
| <a id="pagamento.parcela_paga"></a>`pagamento.parcela_paga` | 409 | Esta parcela já está paga. |
| <a id="pagamento.sem_comprovante"></a>`pagamento.sem_comprovante` | 404 | Este aviso de pagamento não tem comprovante. |

### perfil

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="perfil.foto_ilegivel"></a>`perfil.foto_ilegivel` | 400 | Não foi possível ler esta imagem. Envie outra foto. |
| <a id="perfil.foto_tipo_invalido"></a>`perfil.foto_tipo_invalido` | 400 | Envie uma imagem JPEG, PNG ou WebP. |
| <a id="perfil.foto_vazia"></a>`perfil.foto_vazia` | 400 | Nenhuma imagem foi enviada. |
| <a id="perfil.sem_foto"></a>`perfil.sem_foto` | 404 | Este formando ainda não enviou foto. |

### privacidade

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="privacidade.titular_nao_encontrado"></a>`privacidade.titular_nao_encontrado` | 404 | Titular não encontrado. |
| <a id="privacidade.solicitacao_nao_encontrada"></a>`privacidade.solicitacao_nao_encontrada` | 404 | Solicitação não encontrada. Também é a resposta ao pacote de outro titular, ao ainda não gerado e ao já expirado. |
| <a id="privacidade.senha_obrigatoria"></a>`privacidade.senha_obrigatoria` | 400 | Digite sua senha para confirmar. Só na eliminação, que é irreversível. |
| <a id="privacidade.senha_invalida"></a>`privacidade.senha_invalida` | 401 | Senha incorreta. |
| <a id="privacidade.nada_a_confirmar"></a>`privacidade.nada_a_confirmar` | 409 | Só a solicitação de exclusão precisa de confirmação. |
| <a id="privacidade.solicitacao_encerrada"></a>`privacidade.solicitacao_encerrada` | 409 | Esta solicitação já foi atendida ou cancelada. |
| <a id="privacidade.anonimizacao_recusada"></a>`privacidade.anonimizacao_recusada` | 409 | Não foi possível anonimizar a conta. Tente de novo. |

### recebimento

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="recebimento.conta_ja_conferida"></a>`recebimento.conta_ja_conferida` | 409 | Esta chave já foi conferida. |
| <a id="recebimento.conta_sem_mudanca"></a>`recebimento.conta_sem_mudanca` | 409 | Estes dados são os mesmos da conta atual. |
| <a id="recebimento.sem_conta"></a>`recebimento.sem_conta` | 404 | A turma ainda não cadastrou a chave PIX. |

### usuario

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="usuario.autodesativacao"></a>`usuario.autodesativacao` | 409 | Você não pode desativar o próprio acesso. |
| <a id="usuario.autorrebaixamento"></a>`usuario.autorrebaixamento` | 409 | Você não pode remover o próprio perfil de administrador. |
| <a id="usuario.email_em_uso"></a>`usuario.email_em_uso` | 409 | Já existe uma conta com este e-mail. |
| <a id="usuario.nao_encontrado"></a>`usuario.nao_encontrado` | 404 | Usuário não encontrado. |

### webhook

| Código | Status | Quando acontece |
| --- | --- | --- |
| <a id="webhook.assinatura_invalida"></a>`webhook.assinatura_invalida` | 401 | Assinatura do webhook inválida. |
| <a id="webhook.payload_invalido"></a>`webhook.payload_invalido` | 400 | Corpo do webhook ilegível. |

---

Esta tabela é gerada a partir das chamadas a `Erro.*` no código-fonte. Código novo entra aqui na
mesma alteração que o cria — um `codigo` sem linha nesta tabela é um `type` apontando para uma
âncora que não existe.
