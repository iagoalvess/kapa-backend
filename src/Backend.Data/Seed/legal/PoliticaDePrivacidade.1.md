# Política de Privacidade do Kapa

Esta Política explica quais dados pessoais o Kapa usa, para quê, com quem compartilha e como você exerce seus direitos. Ela segue a Lei Geral de Proteção de Dados (Lei nº 13.709/2018, a "LGPD").

## Palavras que usamos

- **Dado pessoal:** qualquer informação que identifique você, como nome, e-mail ou CPF.
- **Dado sensível:** dado pessoal sobre saúde, religião, origem racial ou étnica, entre outros que a LGPD protege de forma especial (art. 5º, II).
- **Controlador:** quem decide para que os dados são usados.
- **Operador:** quem usa os dados em nome do controlador, seguindo as instruções dele.
- **Base legal:** o motivo, previsto na LGPD, que permite usar um dado.
- **Criptografia:** técnica que embaralha o dado guardado, de forma que só o sistema do Kapa consegue lê-lo.
- **Mascarado:** mostrado só em parte, como um CPF exibido como `***.456.789-**`.
- **Anonimizar:** trocar os dados que identificam você por marcadores, de forma que não seja mais possível saber quem era.

## 1. Quem cuida dos seus dados

A plataforma Kapa é operada por **KAPA FORMATURAS INOVA SIMPLES (I.S.)**, CNPJ nº **69.334.998/0001-67**.

- **Suporte, dúvidas e pedidos sobre dados pessoais:** suporte@kapaformaturas.com.br

## 2. Dois responsáveis: o Kapa e a comissão da sua formatura

No Kapa, os dados pessoais têm dois responsáveis diferentes, conforme o uso:

- **O Kapa é o controlador** dos dados da sua conta (nome, e-mail, senha), dos registros de acesso e dos registros de aceite destes documentos. Também dos dados de contratação e pagamento da assinatura da plataforma.
- **A comissão da sua formatura é a controladora** dos dados usados para organizar a formatura. São eles: cadastro na turma, adesão, cobranças, pagamentos, despesas, convidados, vendas da loja da turma, avisos e documentos. Nesse caso, a **Kapa é operadora**: usa esses dados em nome da comissão, seguindo as instruções dela e esta Política.

Pedidos sobre os dados da formatura podem ser feitos à comissão ou ao Kapa. Se você falar com o Kapa, encaminhamos o pedido e ajudamos a comissão a responder.

## 3. Dados que coletamos

### Sua conta

- **Dados de conta:** nome, e-mail e senha. A senha é guardada de forma que nem o Kapa consegue lê-la (hash irreversível).
- **Entrada com Google (opcional):** se você escolher entrar com Google, recebemos do Google o identificador da sua conta Google, o nome e o e-mail, só para confirmar que é você. Não acessamos contatos, agenda, telefone, fotos ou arquivos, e não guardamos a foto do perfil. Você pode ter também uma senha no Kapa, e remover a autorização no Google não exclui sua conta Kapa.

### A formatura

- **Cadastro na formatura:** informado por você ou pela comissão, como nome completo, CPF, telefone, contato de emergência, curso e foto. O CPF é guardado com criptografia.
- **Dados financeiros da formatura:** contribuições, parcelas, vencimentos, pagamentos, avisos de pagamento, confirmações, baixas manuais e comprovantes, renegociações e o registro da adesão ao termo da turma.
- **Conteúdos enviados:** avisos, documentos e arquivos publicados no mural e no acervo da formatura.

### Convidados

Quem tem convites para os eventos da turma (o formando, a comissão ou quem compra na loja da turma) informa o nome de cada convidado e, se quiser:

- o documento (CPF ou RG);
- o e-mail, para o convite chegar direto ao convidado;
- observações para a comissão, como restrição alimentar ou necessidade de acessibilidade.

As observações podem revelar informação de saúde, que é dado sensível: informe só o necessário para organizar o evento. Documento, e-mail e observações são guardados com criptografia. Quem informa os dados de um convidado declara que ele sabe e concorda.

### Pagamentos

O dinheiro nunca passa por uma conta do Kapa. Os dados que usamos dependem de quem paga e de como.

**Parcelas da formatura, modo manual (PIX na chave da comissão)**

1. A comissão cadastra a conta onde a turma recebe: a chave PIX, o nome do titular e a cidade.
2. Para cada parcela, a plataforma mostra ao formando um código PIX (QR Code e "copia e cola") montado com esses dados.
3. O formando paga pelo aplicativo do banco dele, direto na conta da comissão. O Kapa não vê essa transação e não recebe nenhum dado do banco.
4. Se quiser, o formando avisa na plataforma que pagou, informando a data, o valor e, opcionalmente, o comprovante.
5. A tesouraria confere o extrato do banco e confirma o pagamento. Registramos quem confirmou e quando.

**Parcelas da formatura, modo automático (Mercado Pago da turma)**

1. A comissão conecta a conta Mercado Pago da turma. Guardamos, com criptografia, a chave de acesso que o Mercado Pago entrega nessa conexão.
2. Quando o formando escolhe pagar, o Kapa cria a cobrança na conta Mercado Pago da turma. Para isso, envia ao Mercado Pago **só o e-mail do formando**, o valor e a identificação da parcela. Nome e CPF não são enviados.
3. No PIX, o código é gerado pelo Mercado Pago. No cartão, o formando digita os dados no formulário do próprio Mercado Pago, dentro da página; o Kapa recebe apenas um código que representa o cartão e não revela o número.
4. O dinheiro cai direto na conta Mercado Pago da turma. O Mercado Pago informa ao Kapa a situação do pagamento, o meio usado e o número de parcelas do cartão, e a parcela passa a constar como paga.

**Loja da turma (compra de convites avulsos)**

1. O comprador, formando ou não, informa nome, e-mail, CPF e os dados de cada convidado, e confirma que leu esta Política. Registramos a versão lida, a data, o endereço IP e o navegador.
2. O Kapa cria a cobrança na conta Mercado Pago da turma, enviando o **nome e o e-mail** do comprador. O CPF não é enviado.
3. O pagamento é feito por PIX ou cartão, no Mercado Pago, da mesma forma que nas parcelas.
4. Quando o Mercado Pago informa o CPF de quem efetivamente pagou, guardamos esse CPF com criptografia, para identificar revenda irregular de convites e fraude.
5. Os pedidos de cancelamento da compra também ficam registrados.

**Assinatura do Kapa**

O Presidente paga o plano da formatura por PIX ou cartão, no Mercado Pago, que é o provedor de pagamentos do Kapa. Enviamos ao Mercado Pago o e-mail de quem paga e recebemos a situação de cada pagamento, que usamos para manter o plano e emitir as notas fiscais.

**Chave PIX como dado pessoal.** Quando a chave PIX da turma é um CPF, e-mail ou telefone, ela é dado pessoal do titular da conta e aparece para os membros da turma, que precisam dela para pagar.

### Registros de aceite e de uso

- **Aceite destes documentos do Kapa (Termos de Uso e esta Política):** a versão aceita, a data e a hora, o endereço IP e o navegador. É o que comprova o seu aceite.
- **Assinatura do termo de adesão da turma e dos aditivos:** o termo de adesão **não é um documento do Kapa**: é o contrato entre o formando e a comissão, escrito pela comissão, com os valores e as regras da formatura. Quando o formando assina, registramos o nome e o CPF dele naquele momento, a versão do termo, a data e a hora, o endereço IP, o navegador e uma impressão digital (hash) do texto, que prova qual texto foi assinado. Esses registros pertencem à formatura, e a controladora deles é a comissão (seção 2).
- **Registros de acesso e uso:** endereço IP, data e hora de acesso, navegador e as ações feitas na plataforma (por exemplo, "cobrança criada" ou "membro removido").

### O que não coletamos

- dados de cartão de crédito (digitados sempre no ambiente do Mercado Pago);
- senhas bancárias;
- data de nascimento;
- localização;
- contatos do seu celular;
- dados de redes sociais, além dos descritos em "Entrada com Google".

## 4. Para que usamos os dados e com qual base legal

Cada uso tem um motivo, chamado base legal, previsto na LGPD. As bases usadas pelo Kapa estão no art. 7º da lei:

- **inciso I:** consentimento do titular;
- **inciso II:** cumprimento de obrigação legal ou regulatória;
- **inciso V:** execução de contrato do qual o titular é parte, ou de procedimentos preliminares a ele, a pedido do titular;
- **inciso VI:** exercício regular de direitos em processo judicial, administrativo ou arbitral;
- **inciso IX:** interesse legítimo do controlador ou de terceiro, respeitados os direitos e as liberdades do titular.

Para dados sensíveis, a base é a do art. 11. Nos usos ligados à formatura, a base é a da comissão, que é a controladora; o Kapa atua como operador (seção 2).

**Sua conta e o Kapa**

- **Criar e manter sua conta e confirmar que é você ao entrar, inclusive pela entrada com Google:** execução do contrato com você, que são os Termos de Uso (art. 7º, V).
- **Enviar e-mails sobre a sua conta, a sua formatura, cobranças e pagamentos, inclusive os lembretes de vencimento:** execução do contrato (art. 7º, V).
- **Cobrar a assinatura do Kapa:** execução do contrato (art. 7º, V).
- **Emitir as notas fiscais da assinatura:** obrigação legal (art. 7º, II).
- **Guardar os registros de acesso à plataforma por 6 meses:** obrigação legal (art. 7º, II), imposta pelo art. 15 do Marco Civil da Internet.
- **Registrar e guardar o seu aceite aos Termos de Uso e a esta Política:** exercício regular de direitos, para comprovar o aceite se for preciso (art. 7º, VI).
- **Prevenir fraudes, proteger a plataforma e melhorar o serviço:** interesse legítimo (art. 7º, IX). Você pode se opor a esse uso (seção 10).
- **Gerar o resumo do termo de adesão por inteligência artificial:** interesse legítimo de facilitar a leitura do termo (art. 7º, IX). Vai para a inteligência artificial só o texto do termo, nunca dados de formandos.
- **Enviar novidades e ofertas do próprio Kapa:** consentimento (art. 7º, I), dado no cadastro numa caixa que vem desmarcada. Dá para deixar de receber quando quiser, pelo link presente em toda mensagem. Só membros de comissão recebem; formandos, nunca.
**A formatura (controladora: a comissão)**

- **Organizar a formatura: cadastro, cobranças, confirmação de pagamentos, despesas, caixa e prestação de contas:** execução do contrato entre o formando e a comissão, que é o termo de adesão (art. 7º, V).
- **Gerar o PIX das parcelas ou as cobranças no Mercado Pago da turma, e registrar os pagamentos:** execução do mesmo contrato (art. 7º, V).
- **Registrar a assinatura do termo de adesão e dos aditivos, com nome e CPF:** execução do contrato (art. 7º, V) e exercício regular de direitos, para comprovar a adesão se for preciso (art. 7º, VI).
- **Guardar os registros financeiros da formatura depois do encerramento:** exercício regular de direitos, para a prestação de contas e a defesa em eventual disputa (art. 7º, VI).
- **Mostrar à turma números gerais, como o saldo em caixa e a adimplência:** interesse legítimo na transparência da gestão da formatura (art. 7º, IX). Nenhum formando vê dados individuais de outro.
- **Registrar as compras da loja da turma:** execução do contrato de compra entre o comprador e a comissão (art. 7º, V).
- **Guardar o CPF de quem pagou uma compra da loja, quando o Mercado Pago o informa:** interesse legítimo de prevenir revenda irregular de convites e fraude (art. 7º, IX).
- **Nome, documento e e-mail dos convidados, para emitir os convites e controlar a entrada no evento:** interesse legítimo da comissão na organização e na segurança do evento (art. 7º, IX). O convidado não é parte do contrato; por isso, a base não é a execução de contrato.
- **Observações sobre convidados que revelem saúde, como restrição alimentar ou necessidade de acessibilidade:** consentimento específico e destacado para essa finalidade (art. 11, I), que quem informa a observação declara ter obtido do convidado ou do responsável por ele.

## 5. Quem vê seus dados dentro da formatura

- **Presidente, Tesoureiro e Comissão** veem o cadastro e o extrato dos formandos da turma, para administrá-la, com o CPF mascarado. O número completo aparece só para o próprio formando e no termo de adesão que ele assina.
- **Formandos** veem só os próprios dados, os números gerais da turma e a chave PIX e o nome do titular da conta de recebimento, para poder pagar.
- **Convidados:** quem informou vê os dados dos próprios convites. A comissão vê a lista da turma com o documento mascarado, inclusive na portaria, e as observações. O documento completo aparece só na lista que a comissão exporta para a entrada do evento.
- **Compradores da loja:** a comissão vê os dados da compra e dos convidados, com o CPF e os documentos mascarados.
- Pessoas de uma formatura **não** veem dados de outra formatura.

## 6. Com quem compartilhamos

O Kapa **não vende** dados pessoais e não os compartilha para publicidade. Compartilhamos dados só com os fornecedores abaixo, para que o serviço funcione, e com autoridades quando a lei exige.

- **Mercado Pago:** cobra a assinatura do Kapa. Quando a comissão conecta a conta da turma, cria nela as cobranças das parcelas e as vendas da loja. Recebe do Kapa o e-mail de quem paga (nas compras da loja, também o nome) e os dados da cobrança; os dados do cartão são digitados direto no formulário dele. Nas cobranças da turma, o dinheiro vai direto para a conta dela, e o Mercado Pago também decide sobre esses dados como controlador, conforme a política dele.
- **Cloudflare:** publica o site e o aplicativo, guarda os arquivos enviados e gerados (fotos, comprovantes, documentos, relatórios).
- **MillionSend:** envia os e-mails da plataforma. Recebe nome e e-mail do destinatário e o conteúdo da mensagem.
- **Grafana Cloud:** guarda registros técnicos de funcionamento e de erro do sistema, que podem conter endereço IP e identificadores internos.
- **OpenRouter:** fornece os modelos de inteligência artificial que geram o resumo do termo de adesão. Recebe só o texto do termo escrito pela comissão, que pode conter razão social, CNPJ, endereço e nomes dos membros da comissão. Não recebe nenhum dado de formando: nem nome, CPF, e-mail ou identificador. O Kapa pede ao OpenRouter que use só provedores que não guardam o texto nem o usam para treinar seus modelos.
- **Railway:** executa a aplicação e guarda o banco de dados, em servidores nos Estados Unidos. Recebe todos os dados da plataforma; CPF, documentos e observações de convidados já chegam com a criptografia do Kapa.
- **Autoridades públicas:** quando a lei ou uma ordem judicial exigir.

Esses fornecedores são operadores: usam os dados só para prestar o serviço contratado pelo Kapa, com obrigações de sigilo e segurança. O Kapa continua responsável por eles (LGPD, art. 42). A lista atualizada fica publicada na plataforma e pode ser pedida ao suporte.

## 7. Dados fora do Brasil

Alguns fornecedores guardam dados fora do Brasil, principalmente nos Estados Unidos e na União Europeia:

- Railway, onde ficam a aplicação e o banco de dados;
- Cloudflare, Grafana Cloud, MillionSend e OpenRouter;
- os provedores de modelos para os quais o OpenRouter envia o texto do termo.

Essas transferências seguem o art. 33 da LGPD e são protegidas pelas cláusulas-padrão de contrato aprovadas pela Autoridade Nacional de Proteção de Dados (Resolução CD/ANPD nº 19/2024) ou por outro mecanismo previsto em lei.

## 8. Cookies

O Kapa usa só **cookies essenciais**: o cookie de sessão, que mantém você conectado e não pode ser lido por scripts da página (HttpOnly). Não usamos cookies de publicidade nem ferramentas de análise de terceiros.

Duas funções carregam código de outras empresas, que pode gravar seus próprios cookies ou identificadores, necessários para funcionar com segurança:

- **pagamento por cartão:** o formulário do Mercado Pago, que identifica o aparelho para prevenir fraude;
- **entrada com Google:** o botão do Google, carregado só nas telas de entrada e cadastro do aplicativo.

Se isso mudar, esta Política será atualizada antes.

## 9. Por quanto tempo guardamos

- **Dados de conta:** enquanto a conta existir.
- **Registros de acesso:** 6 meses (Marco Civil da Internet, art. 15).
- **Registros de ações de uso:** até 180 dias.
- **Registros financeiros da formatura:** durante a formatura e por 5 anos depois do encerramento, para a prestação de contas e as obrigações legais. Na prática:
  - a turma encerrada é guardada por 5 anos e depois eliminada;
  - a turma suspensa por falta de pagamento é encerrada depois de 12 meses;
  - a turma descartada pela comissão antes de contratar é eliminada em 30 dias.
- **Dados de pagamento da assinatura do Kapa:** 5 anos, por exigência fiscal.
- **Aceite dos Termos de Uso e desta Política:** enquanto a conta existir e, depois, pelo tempo necessário para defender direitos.
- **Assinaturas do termo de adesão e dos aditivos (com nome e CPF do formando):** junto dos registros financeiros da formatura, durante a formatura e por 5 anos depois do encerramento, mesmo que a conta seja excluída.- **Compras da loja (dados do comprador, CPF de quem pagou e pagamento):** junto dos registros financeiros da formatura, durante a formatura e por 5 anos depois do encerramento.
- **Dados de convidados, inclusive os informados numa compra da loja:** documento, e-mail e observações são eliminados 30 dias depois do evento. O nome fica como histórico da formatura.

### Exclusão de conta

Você pode solicitar a exclusão pelo suporte@kapaformaturas.com.br, mesmo sem acesso à conta. Confirmamos sua identidade de forma proporcional e analisamos o alcance do pedido. Não pedimos sua senha por e-mail.

Encerramos o acesso e eliminamos os dados que não precisam mais ser tratados. Registros financeiros, contratuais, de aceite e de segurança podem ser conservados quando houver fundamento legal e pelo prazo necessário, com acesso restrito. Informamos quais dados foram eliminados ou conservados e o motivo. A retirada dos identificadores do cadastro não torna anônimos os documentos contratuais conservados.

Pedidos relativos aos dados da formatura são tratados com a comissão responsável. A exclusão da conta não cancela contratos nem extingue obrigações financeiras.

## 10. Seus direitos

Pela LGPD (art. 18), você pode pedir:

- confirmação de que usamos seus dados, e acesso a eles;
- correção de dados incompletos, errados ou desatualizados;
- anonimização, bloqueio ou eliminação de dados desnecessários, excessivos ou usados em desacordo com a lei;
- cópia dos seus dados para levar a outro fornecedor (portabilidade);
- eliminação dos dados usados com base no seu consentimento;
- informação sobre com quem compartilhamos seus dados;
- informação sobre a possibilidade de não consentir e o que acontece nesse caso;
- revogação do consentimento;
- revisão de decisões tomadas só por meio automático.

Você pode corrigir seu cadastro diretamente na plataforma. Para os demais pedidos, escreva para **suporte@kapaformaturas.com.br**. O atendimento é gratuito e não exige uma conta ativa. Podemos confirmar sua identidade ou a representação legal antes de fornecer dados ou executar alterações.

A confirmação ou o acesso simplificado são providenciados imediatamente; a declaração completa de confirmação/acesso é fornecida em até 15 dias, ressalvados os prazos diferenciados legalmente aplicáveis. Os demais pedidos seguem os prazos aplicáveis, e informamos quando alguma providência não puder ser adotada imediatamente e por quê. Você também pode reclamar à Autoridade Nacional de Proteção de Dados (ANPD).

## 11. Decisões automáticas

- **Lembretes de vencimento:** são automáticos e seguem a situação das parcelas e as regras da comissão, sem análise de perfil.
- **Confirmação de pagamentos:** no modo manual, é feita pela tesouraria da turma. No modo automático, segue a confirmação do Mercado Pago, sem análise de perfil.
- **Resumo do termo de adesão:** é gerado automaticamente por inteligência artificial, sem revisão de uma pessoa, e só ajuda a leitura. Vale o texto completo do termo, e o resumo não faz parte do que você aceita.

Se discordar de uma cobrança ou de uma confirmação, fale com a comissão da sua formatura ou com o suporte do Kapa.

## 12. Menores de idade

A plataforma é só para maiores de 18 anos, e contas de menores são encerradas quando identificadas.

Convidados da festa podem ser menores de idade. Os dados deles são informados por um adulto responsável pelo convite, usados só para o controle de entrada do evento, sempre no melhor interesse do menor (LGPD, art. 14), e eliminados 30 dias depois do evento, menos o nome.

## 13. Segurança

Protegemos seus dados com medidas técnicas e de organização, entre elas:

- conexão protegida por criptografia (TLS) entre o seu aparelho e o Kapa;
- senhas e tokens de sessão guardados de forma que não podem ser lidos (hash);
- CPF guardado com criptografia e mostrado mascarado para a comissão;
- documentos e observações dos convidados, e a chave de acesso ao Mercado Pago da turma, guardados com criptografia;
- sessão em cookie que scripts da página não conseguem ler;
- código extra enviado por e-mail na entrada do Presidente e da equipe do Kapa;
- confirmação por link no e-mail para trocar a chave PIX, conectar o Mercado Pago ou nomear um novo Presidente;
- dados de cada formatura separados e acesso controlado pelo papel de cada pessoa;
- registro das operações financeiras sensíveis, como confirmações de pagamento, baixas manuais, trocas da chave PIX e mudanças de papel.

Nenhum sistema é totalmente seguro, e não podemos garantir que os dados nunca sejam alvo de acesso não autorizado por meios ilegais.

## 14. Incidentes de segurança

Se acontecer um incidente que possa trazer risco ou dano relevante, avisaremos a ANPD e as pessoas afetadas em até 3 dias úteis (Resolução CD/ANPD nº 15/2024). O aviso diz quais dados foram afetados, os riscos envolvidos e o que foi feito.

## 15. Mudanças nesta Política

Esta Política não é editada: cada mudança vira uma nova versão, e as versões anteriores continuam disponíveis, cada uma em seu endereço permanente. Quando sair uma nova versão, você será levado a lê-la e aceitá-la no próximo acesso, antes de continuar usando a plataforma.
