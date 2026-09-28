using System.Collections.Concurrent;

namespace Backend.Business.Auth.Services;

/// <summary>
/// Senhas erradas por par conta + origem, para barrar o palpite seguinte sem trancar a conta inteira.
/// </summary>
/// <remarks>
/// Substitui o lockout do Identity, que contava por conta: cinco senhas erradas de qualquer lugar
/// trancavam o dono por 15 minutos, e bastava saber o e-mail do presidente para tirá-lo do ar. Contando
/// por origem, quem erra só tranca a si mesmo; o dono entra de outra rede. O limite de requisições por
/// IP (<c>RateLimit:AutenticacaoPorMinuto</c>) segue segurando a força bruta de uma origem só.
/// <para>
/// <c>ponytail:</c> em memória, por processo — com N réplicas o teto vira 5×N por janela, e reiniciar
/// zera a conta. Vai para o banco ou um cache compartilhado se isso passar a importar.
/// </para>
/// </remarks>
public sealed class TentativasDeSenha
{
    /// <summary>Quantas senhas erradas a origem pode tentar dentro da janela.</summary>
    public const int Maximo = 5;

    /// <summary>Quanto tempo a contagem vale, a partir do primeiro erro.</summary>
    public static readonly TimeSpan Janela = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Teto de pares guardados. <c>ponytail:</c> passou dele, a memória é varrida e, se ainda cheia,
    /// esvaziada — um ataque com milhões de e-mails perde a contagem, mas não derruba o processo.
    /// </summary>
    private const int TetoDePares = 100_000;

    private readonly ConcurrentDictionary<string, (int Falhas, DateTime Desde)> _falhas = new(StringComparer.Ordinal);

    /// <summary>Se a origem já errou o bastante para esta conta.</summary>
    /// <param name="conta">E-mail (login) ou id do usuário.</param>
    /// <param name="origem">IP de quem tenta, ou outro recorte que separe as origens.</param>
    public bool Bloqueada(string conta, string? origem) =>
        _falhas.TryGetValue(Chave(conta, origem), out var registro) && registro.Falhas >= Maximo && DateTime.UtcNow - registro.Desde < Janela;

    /// <summary>Conta mais uma senha errada da origem.</summary>
    /// <param name="conta">E-mail (login) ou id do usuário.</param>
    /// <param name="origem">IP de quem tenta.</param>
    public void RegistrarFalha(string conta, string? origem)
    {
        if (_falhas.Count >= TetoDePares)
            Varrer();

        var agora = DateTime.UtcNow;
        _falhas.AddOrUpdate(
            Chave(conta, origem),
            _ => (1, agora),
            (_, registro) => agora - registro.Desde >= Janela ? (1, agora) : (registro.Falhas + 1, registro.Desde)
        );
    }

    /// <summary>Esquece os erros da origem — depois de acertar a senha.</summary>
    /// <param name="conta">E-mail (login) ou id do usuário.</param>
    /// <param name="origem">IP de quem acertou.</param>
    public void Limpar(string conta, string? origem) => _falhas.TryRemove(Chave(conta, origem), out _);

    private void Varrer()
    {
        var agora = DateTime.UtcNow;

        foreach (var (chave, registro) in _falhas)
            if (agora - registro.Desde >= Janela)
                _falhas.TryRemove(chave, out _);

        if (_falhas.Count >= TetoDePares)
            _falhas.Clear();
    }

    private static string Chave(string conta, string? origem) => $"{conta.Trim().ToUpperInvariant()}|{origem}";
}
