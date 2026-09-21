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
/// Em código, e não em configuração: esta lista precisa bater com a seção correspondente da
/// Política de Privacidade, que é uma linha versionada em <c>documentos_legais</c>. Duas fontes
/// editáveis em lugares diferentes discordam no dia em que alguém troca de provedor com pressa.
/// Trocar de operador é publicar versão nova da Política <b>e</b> mudar aqui, no mesmo commit.
/// </para>
/// <para>
/// <c>ponytail:</c> quando o PSP da assinatura for escolhido (P2 da Sprint 16), o nome dele entra
/// no lugar de "Provedor de pagamento"; e o resumo por IA da Sprint 23 acrescenta a linha do
/// provedor de modelo, que é o que a decisão 12 de lá exige.
/// </para>
/// </remarks>
public static class OperadoresDaKapa
{
    /// <summary>A lista publicada em <c>/privacidade/operadores</c>.</summary>
    public static readonly IReadOnlyList<Operador> Todos =
    [
        new("Provedor de hospedagem", "Executar a aplicação e guardar o banco de dados.", "Todos os dados da plataforma, cifrados em repouso."),
        new(
            "Provedor de e-mail",
            "Entregar confirmação de conta, redefinição de senha, avisos de cobrança e as mensagens da régua.",
            "Nome e endereço de e-mail do destinatário, e o conteúdo da mensagem."
        ),
        new(
            "Provedor de armazenamento de arquivos",
            "Guardar foto de perfil, comprovante de pagamento, documento do acervo e os arquivos gerados pela plataforma.",
            "Os arquivos enviados pelos usuários e os relatórios e exportações gerados."
        ),
        new(
            "Provedor de pagamento",
            "Cobrar a assinatura da plataforma da comissão. O dinheiro dos formandos não passa por ele: ele vai direto para a chave PIX informada pela comissão.",
            "Dados de cobrança de quem contrata a assinatura."
        ),
    ];
}
