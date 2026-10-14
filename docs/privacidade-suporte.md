# Atendimento de privacidade pelo suporte

Desde 13/10/2026, antes do lançamento, pedidos sobre dados pessoais são recebidos em
**suporte@kapaformaturas.com.br**. O aplicativo oferece esse contato no menu de suporte e a
Política de Privacidade o divulga publicamente, inclusive para quem não possui conta.
Não há portal autenticado, endpoints de exportação/exclusão, fila de solicitações ou job de privacidade.

## Procedimento

1. Guarde o pedido e a data de recebimento. Identifique o direito e o alcance solicitados.
2. Confirme a identidade ou a representação de forma proporcional. Nunca peça a senha por e-mail.
3. Para dados da formatura, envolva a comissão controladora. Trate os dados da conta sob responsabilidade do Kapa.
4. Atenda o pedido ou explique os limites, os fundamentos de conservação e as próximas providências.
5. Registre o que foi feito e a resposta. O e-mail do suporte pode ser o próprio registro de atendimento.

A confirmação/acesso simplificado segue atendimento imediato; a declaração completa tem prazo
geral de até 15 dias, observadas as flexibilizações legalmente aplicáveis. Não se deve aplicar
uma espera automática de 15 dias à exclusão. Os demais direitos seguem os prazos aplicáveis.
O atendimento não exige assinatura paga, conta ativa ou acesso ao aplicativo.

## Comandos internos

Execute na pasta `backend`, com a configuração do ambiente pretendido. O comando usa o mesmo
acesso PostgreSQL e a mesma chave de criptografia da aplicação, mas **não inicia os jobs**, não
envia e-mails e não publica arquivos. Segredos ficam na configuração, não nos argumentos.

```powershell
dotnet run --project src/Backend.Worker -- privacidade exportar UUID_DO_TITULAR C:\pasta-restrita\dados.zip
dotnet run --project src/Backend.Worker -- privacidade novidades UUID_DO_TITULAR parar
dotnet run --project src/Backend.Worker -- privacidade novidades UUID_DO_TITULAR receber
dotnet run --project src/Backend.Worker -- privacidade excluir-cadastro UUID_DO_TITULAR --confirmar
```

Substitua o UUID pelo identificador da conta verificada. A pasta de destino deve existir e ter
acesso restrito. Um arquivo existente não é sobrescrito. Código de saída 0 indica conclusão,
1 indica falha/recusa e 2 indica argumentos ou destino inválidos.

### Exportação

O ZIP reúne dados da conta e de **todos os vínculos**, ativos e inativos: cadastros, parcelas,
aceites, respostas a enquetes e histórico de marketing, em JSON e CSV. Usa a consulta existente
do repositório e não inclui hashes de senha ou tokens. As chaves de criptografia são usadas
pela aplicação para entregar campos legíveis, evitando exportar o CPF cifrado diretamente por SQL.

**O ZIP não é uma resposta completa para todo pedido possível.** Avalie separadamente fotos,
comprovantes, documentos assinados, compras avulsas, dados de convidados, registros técnicos
e dados mantidos por fornecedores. Complemente quando pertinentes, protegendo os dados de
terceiros. Para quem não possui conta, o comando por UUID não atende: localize os registros
de compra/convite/lista de espera correspondentes e prepare a resposta manualmente.

Revise o arquivo antes da entrega. Use um canal seguro compatível com o conteúdo e confirme
o destinatário. Informe também origem, finalidades, critérios e compartilhamentos quando
pertinentes ao pedido; enviar apenas uma planilha não substitui esses esclarecimentos.
Elimine a cópia temporária após a entrega e o período operacional necessário.

### Limpeza cadastral

Execute apenas após verificar o titular, analisar as retenções e coordenar os dados da turma
com a comissão. A confirmação na linha de comando é obrigatória. A ferramenta desativa a
conta, retira o marketing, limpa nome/e-mail/telefone da conta, remove a senha, limpa perfis,
remove a foto e desassocia respostas às enquetes, preservando os resultados agregados.

Vínculos, lançamentos e provas de adesão/aceite permanecem. Isso **não é anonimização integral**:
documentos contratuais retidos permitem identificar o titular. Avalie os demais registros,
arquivos, identidades externas e fornecedores separadamente, e informe ao titular o que ficou,
por qual fundamento e por quanto tempo. Não prometa exclusão total só porque o comando concluiu.

### Marketing

O consentimento opcional continua no cadastro. Todo e-mail de marketing mantém “Não quero mais
receber” no rodapé e os cabeçalhos `List-Unsubscribe` / `List-Unsubscribe-Post`.
O descadastro é público, sem login, e confirma a ação por POST para que visitas de antivírus
ao link não removam a preferência. Uma solicitação ao suporte também pode ser atendida pelos
comandos acima; registre `receber` somente quando houver autorização expressa do titular.
Avisos necessários à conta e à formatura não são desativados pelo descadastro de marketing.

## Banco de desenvolvimento

A migration `RemovePortalDePrivacidade` remove a tabela `solicitacoes_de_privacidade`, a coluna de revogação dos aceites e ajusta
os textos da versão 1 dos documentos legais, ainda não publicada em produção. Não recria nem
apaga os demais dados do banco. O gatilho de imutabilidade dos documentos é reativado na mesma
transação depois do ajuste. Documentos publicados após o lançamento exigirão novas versões.

```powershell
dotnet ef database update --project src/Backend.Data --startup-project src/Backend.Api
```
