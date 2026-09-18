using Backend.Business.Abstractions;

namespace Backend.Business.Leads.Models;

/// <summary>
/// Um contato deixado no formulário da página institucional.
/// </summary>
/// <remarks>
/// Pertence à <b>plataforma</b>, e não a uma turma: quem preenche ainda não tem formatura — é
/// justamente por isso que preenche. Por isso herda de <see cref="Entity"/> e não de
/// <c>EntidadeDaFormatura</c>.
/// <para>
/// <b>Quem deixa um lead também é titular de dados.</b> Nome, e-mail e telefone são dado pessoal
/// antes de existir conta, e a LGPD não espera o cadastro. O consentimento vai gravado aqui
/// (<see cref="PrivacidadeVersao"/>, <see cref="ConsentidoEm"/>, <see cref="EnderecoIp"/>,
/// <see cref="UserAgent"/>) com os mesmos campos de prova de <c>ConsentimentoRegistrado</c>, da
/// Sprint 1 — mas na própria linha, porque aquela tabela é indexada por conta de usuário e aqui
/// não há nenhuma. Mesma máquina, sem inventar um usuário fantasma para ela.
/// </para>
/// </remarks>
public class Lead : Entity
{
    /// <summary>Nome de quem preencheu.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>E-mail de contato, em minúsculas.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Telefone em E.164, quando reconhecido.</summary>
    public string? Telefone { get; set; }

    /// <summary>Instituição de ensino.</summary>
    public string Instituicao { get; set; } = string.Empty;

    /// <summary>Curso.</summary>
    public string Curso { get; set; } = string.Empty;

    /// <summary>Quantos formandos a turma tem. É o que decide qual plano cabe.</summary>
    public int TamanhoDaTurma { get; set; }

    /// <summary>Quando a turma cola grau, no primeiro dia do mês previsto. Nulo se não informado.</summary>
    /// <remarks>
    /// Mês e ano bastam: ninguém que ainda está escolhendo assessoria sabe o dia. Guardado como data
    /// para o comercial poder ordenar por urgência.
    /// </remarks>
    public DateOnly? PrevisaoDeColacao { get; set; }

    /// <summary>Uma linha livre com o que a pessoa quiser contar. Nulo quando ela não escreveu nada.</summary>
    public string? Mensagem { get; set; }

    /// <summary>De onde veio a visita: <c>utm_source</c>.</summary>
    public string? Origem { get; set; }

    /// <summary>Meio da campanha: <c>utm_medium</c>.</summary>
    public string? Meio { get; set; }

    /// <summary>Campanha: <c>utm_campaign</c>.</summary>
    public string? Campanha { get; set; }

    /// <summary>Versão da Política de Privacidade consentida no envio.</summary>
    public string PrivacidadeVersao { get; set; } = string.Empty;

    /// <summary>Momento do consentimento, em UTC.</summary>
    public DateTime ConsentidoEm { get; set; }

    /// <summary>IP de onde veio o envio — parte da prova do consentimento.</summary>
    public string? EnderecoIp { get; set; }

    /// <summary>Navegador que enviou — parte da prova do consentimento.</summary>
    public string? UserAgent { get; set; }
}
