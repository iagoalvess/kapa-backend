namespace Backend.Business.Privacidade.Models;

/// <summary>
/// Com quem a Kapa compartilha dado pessoal, e para quê.
/// </summary>
/// <remarks>
/// Atende ao art. 18, VII — informação sobre compartilhamento — e é o único lugar do código onde
/// essa lista existe. A rota que a publica é <b>anônima</b> de propósito: quem ainda está decidindo
/// se cria conta tem direito de saber para onde o dado dele vai, e uma lista atrás do login só é
/// legível por quem já concordou.
/// <para>
/// Em código, e não em configuração: esta lista precisa bater com a seção 6 da Política de
/// Privacidade, que é uma linha versionada em <c>documentos_legais</c>. Duas fontes editáveis em
/// lugares diferentes discordam no dia em que alguém troca de provedor com pressa. Trocar de operador
/// é publicar versão nova da Política <b>e</b> mudar aqui, no mesmo commit.
/// </para>
/// <para>
/// <c>ponytail:</c> o provedor de hospedagem da API e do banco ainda não foi contratado; quando for, o
/// nome dele entra aqui e na Política.
/// </para>
/// </remarks>
public static class OperadoresDaKapa
{
    /// <summary>A lista publicada em <c>/privacidade/operadores</c>.</summary>
    public static readonly IReadOnlyList<Operador> Todos =
    [
        new("Provedor de hospedagem", "Executar a aplicação e guardar o banco de dados.", "Todos os dados da plataforma, cifrados em repouso."),
        new(
            "Mercado Pago",
            "Cobrar a assinatura da Kapa e, quando a comissão conecta a conta da turma, criar as cobranças das parcelas e as vendas da loja na conta Mercado Pago da turma. O dinheiro da turma vai direto para a conta dela.",
            "Nome, e-mail e CPF de quem paga, e o valor e a descrição da cobrança."
        ),
        new(
            "Cloudflare",
            "Publicar o site e o aplicativo, guardar os arquivos enviados e gerados, verificar que não é um robô e guardar a lista de espera.",
            "Fotos, comprovantes, documentos, relatórios e exportações; endereço IP de quem acessa; os dados informados na lista de espera."
        ),
        new(
            "MillionSend",
            "Entregar confirmação de conta, códigos de acesso, avisos de cobrança e as mensagens da régua.",
            "Nome e endereço de e-mail do destinatário, e o conteúdo da mensagem."
        ),
        new(
            "Grafana Cloud",
            "Guardar os registros técnicos de funcionamento e de erro do sistema.",
            "Endereço IP e identificadores internos que aparecem nos registros."
        ),
        new(
            "OpenRouter",
            "Gerar o resumo do termo de adesão exibido ao formando antes do aceite, só com provedores de modelo que não guardam o texto.",
            "O texto do termo redigido pela comissão (razão social, CNPJ, endereço e nomes da comissão). Nenhum dado de formando."
        ),
    ];
}
