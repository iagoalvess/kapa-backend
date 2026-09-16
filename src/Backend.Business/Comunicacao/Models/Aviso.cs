using Backend.Business.Abstractions;

namespace Backend.Business.Comunicacao.Models;

/// <summary>
/// Comunicado do mural: texto com data, autor e destaque.
/// </summary>
/// <remarks>
/// Só a comissão publica; o formando lê (decisão 1). Sem comentário, reação nem resposta — o mural
/// existe para substituir o grupo de WhatsApp, e não para virar outro.
/// <para>
/// O conteúdo é markdown cru (decisão 2). Nada aqui o interpreta: quem renderiza é o front, com
/// sanitização, e o que se grava é exatamente o que a comissão digitou.
/// </para>
/// <para>
/// Foi a primeira entidade isolada do produto (Sprint 0), e continua sendo a que o teste de
/// isolamento exercita.
/// </para>
/// </remarks>
public class Aviso : EntidadeDaFormatura
{
    /// <summary>Máximo de avisos fixados ao mesmo tempo: sem limite, tudo é fixado e nada é destaque.</summary>
    public const int LimiteDeFixados = 3;

    /// <summary>Título, a primeira coisa do cartão.</summary>
    public string Titulo { get; private set; } = string.Empty;

    /// <summary>Texto em markdown.</summary>
    public string Conteudo { get; private set; } = string.Empty;

    /// <summary>Para quem aparece.</summary>
    public Visibilidade Visibilidade { get; private set; } = Visibilidade.Turma;

    /// <summary>Se fica no topo do mural. No máximo <see cref="LimiteDeFixados"/> por turma.</summary>
    public bool Fixado { get; private set; }

    /// <summary>
    /// Se é importante — o selo no cartão, e o que a régua da Sprint 13 passa a notificar.
    /// </summary>
    /// <remarks>Diferente de <see cref="Fixado"/>: fixar é posição no mural; destaque é urgência.</remarks>
    public bool Destaque { get; private set; }

    /// <summary>Quando foi publicado, em UTC. Não muda na correção.</summary>
    public DateTime PublicadoEm { get; private set; }

    /// <summary>Quem publicou.</summary>
    public Guid PublicadoPorUsuarioId { get; private set; }

    /// <summary>Um aviso novo, publicado agora.</summary>
    /// <param name="dados">Dados já validados.</param>
    /// <param name="autorId">Quem publica.</param>
    public static Aviso Novo(DadosDoAviso dados, Guid autorId)
    {
        var aviso = new Aviso { PublicadoPorUsuarioId = autorId };

        aviso.PublicadoEm = aviso.CriadoEm;
        aviso.Aplicar(dados);

        return aviso;
    }

    /// <summary>
    /// Corrige o aviso.
    /// </summary>
    /// <remarks>Autor e data de publicação ficam: a correção aparece em <c>AtualizadoEm</c>.</remarks>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDoAviso dados)
    {
        Titulo = dados.Titulo.Trim();
        Conteudo = dados.Conteudo.Trim();
        Visibilidade = dados.Visibilidade!.Value;
        Fixado = dados.Fixado;
        Destaque = dados.Destaque;
    }
}
