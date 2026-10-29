using System.Security.Cryptography;
using Backend.Business.Abstractions;

namespace Backend.Business.Festa.Models;

/// <summary>
/// Uma pessoa de fora da turma liberada para validar a entrada de um evento — o segurança, o cerimonial.
/// </summary>
/// <remarks>
/// Sem conta e sem vaga no plano (15/10/2026): a Gestão cadastra o nome, manda o link, e o primeiro
/// celular que o abre vira portaria daquele evento. O link vale uma vez; depois disso o celular guarda a
/// credencial, que só alcança a portaria do evento e morre no fim da janela de validação.
/// <para>
/// O id deste acesso é o que vai em <c>check_ins.validado_por_usuario_id</c> quando é ele quem valida: a
/// coluna não tem chave estrangeira, e quem lê o nome junta com usuários <b>e</b> com acessos.
/// </para>
/// </remarks>
public class AcessoDaPortaria : EntidadeDaFormatura
{
    /// <summary>Evento cuja porta este acesso valida.</summary>
    public Guid EventoId { get; private set; }

    /// <summary>Como a comissão chama a pessoa — "João", "Segurança 2".</summary>
    public string Nome { get; private set; } = string.Empty;

    /// <summary>
    /// O segredo do link enquanto ninguém o abriu; nulo depois da ativação, que o gasta.
    /// </summary>
    /// <remarks>
    /// <c>ponytail:</c> guardado em claro, e não em hash, porque a Gestão precisa copiar o link de novo até a
    /// pessoa abrir. Vale um evento, uma vez, e some na ativação.
    /// </remarks>
    public string? Token { get; private set; }

    /// <summary>Quando o link foi aberto e o celular, liberado.</summary>
    public DateTime? AtivadoEm { get; private set; }

    /// <summary>Quando a Gestão cortou o acesso.</summary>
    public DateTime? RevogadoEm { get; private set; }

    /// <summary>Quem da Gestão liberou.</summary>
    public Guid CriadoPorUsuarioId { get; private set; }

    /// <summary>Um acesso novo, com link ainda por abrir.</summary>
    /// <param name="eventoId">Evento da portaria.</param>
    /// <param name="nome">Nome da pessoa, já validado.</param>
    /// <param name="usuarioId">Quem libera.</param>
    public static AcessoDaPortaria Novo(Guid eventoId, string nome, Guid usuarioId) =>
        new()
        {
            EventoId = eventoId,
            Nome = nome.Trim(),
            Token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)),
            CriadoPorUsuarioId = usuarioId,
        };

    /// <summary>Gasta o link: daqui em diante só a credencial do celular vale.</summary>
    /// <param name="agora">Instante, em UTC.</param>
    public void Ativar(DateTime agora)
    {
        AtivadoEm = agora;
        Token = null;
    }

    /// <summary>Corta o acesso. O link que não foi aberto morre junto.</summary>
    /// <param name="agora">Instante, em UTC.</param>
    public void Revogar(DateTime agora)
    {
        RevogadoEm ??= agora;
        Token = null;
    }
}

/// <summary>Em que pé está um acesso, como a Gestão o vê.</summary>
public enum SituacaoDoAcesso
{
    /// <summary>Link enviado, ninguém abriu.</summary>
    Aguardando,

    /// <summary>Um celular está liberado.</summary>
    Ativo,
}

/// <summary>Um acesso como a lista da Gestão o mostra.</summary>
/// <param name="Id">Acesso.</param>
/// <param name="Nome">Nome da pessoa.</param>
/// <param name="Situacao">Aguardando ou ativo.</param>
/// <param name="Token">O segredo do link, só enquanto aguarda.</param>
/// <param name="AtivadoEm">Quando o celular foi liberado.</param>
/// <param name="CriadoEm">Quando a Gestão liberou.</param>
public sealed record AcessoNaLista(Guid Id, string Nome, SituacaoDoAcesso Situacao, string? Token, DateTime? AtivadoEm, DateTime CriadoEm);

/// <summary>A credencial que o celular da portaria guarda depois de abrir o link.</summary>
/// <param name="AcessoId">Acesso — é o "quem validou" das entradas deste celular.</param>
/// <param name="Nome">Nome da pessoa.</param>
/// <param name="Token">O token que vai no <c>Authorization</c>.</param>
/// <param name="ExpiraEm">Fim da janela de validação, em UTC.</param>
/// <param name="Evento">O evento desta porta.</param>
public sealed record CredencialDaPortaria(Guid AcessoId, string Nome, string Token, DateTime ExpiraEm, EventoDoConvite Evento);
