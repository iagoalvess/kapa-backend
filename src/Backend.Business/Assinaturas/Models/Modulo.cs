namespace Backend.Business.Assinaturas.Models;

/// <summary>
/// Os módulos que um plano libera. É o código que vai em <see cref="Plano.Modulos"/>.
/// </summary>
/// <remarks>
/// Até 18/09/2026 <c>Modulos</c> era texto de vitrine e nada o aplicava — o plano dizia o que
/// incluía e a API liberava tudo igual. Com o plano gratuito isso deixou de ser sustentável: ele
/// existe justamente para dar menos.
/// <para>
/// <b>Código, e não o nome exibido.</b> O rótulo do card muda com a copy, e gate que depende de
/// texto de vitrine se abre no dia em que alguém corrige uma vírgula. O nome mora no front, em
/// <c>config/planos.ts</c>, como os ícones.
/// </para>
/// <para>
/// <b>Mudar o que um plano libera é editar a linha do catálogo</b>, não este arquivo: aqui só
/// entra módulo novo, quando nascer uma área nova do produto.
/// </para>
/// </remarks>
public static class Modulo
{
    /// <summary>Membros, convites e a montagem da comissão.</summary>
    public const string Membros = "membros";

    /// <summary>Termo de adesão e o plano de pagamento da turma.</summary>
    public const string Termo = "termo";

    /// <summary>Cobranças e parcelas.</summary>
    public const string Cobrancas = "cobrancas";

    /// <summary>Recebimento PIX, informes e conferência.</summary>
    public const string Pix = "pix";

    /// <summary>Despesas e fornecedores.</summary>
    public const string Despesas = "despesas";

    /// <summary>Caixa, dashboard e relatórios.</summary>
    public const string Caixa = "caixa";

    /// <summary>Mural, acervo de documentos e a festa.</summary>
    public const string Mural = "mural";

    /// <summary>Avisos, notificações e a régua de cobrança.</summary>
    public const string Avisos = "avisos";

    /// <summary>Painel e exportação contábil.</summary>
    public const string Contabil = "contabil";

    /// <summary>Portal LGPD e trilha de auditoria.</summary>
    public const string Auditoria = "auditoria";

    /// <summary>Todos os códigos. É sobre esta lista que as políticas são registradas.</summary>
    public static readonly IReadOnlyList<string> Todos = [Membros, Termo, Cobrancas, Pix, Despesas, Caixa, Mural, Avisos, Contabil, Auditoria];
}
