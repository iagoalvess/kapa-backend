namespace Backend.Business.Recebimentos.Models;

/// <summary>A chave PIX da turma, como a comissão informa.</summary>
/// <param name="TipoDeChave">Tipo da chave.</param>
/// <param name="Chave">Chave, com ou sem máscara.</param>
/// <param name="NomeDoTitular">Nome do titular, como o banco mostra.</param>
/// <param name="Cidade">Cidade do titular.</param>
public sealed record ChavePixDaConta(TipoDeChavePix TipoDeChave, string Chave, string NomeDoTitular, string Cidade);

/// <summary>
/// A conta para quem vai transferir — banco, agência, conta, tipo e titular.
/// </summary>
/// <remarks>
/// Texto livre, sem dígito conferido (P5 de 21/09/2026): o Kapa não transfere nada, só mostra o que
/// a comissão digitou, e uma validação errada aqui travaria um dado que o banco aceitaria.
/// </remarks>
/// <param name="Banco">Nome do banco, como a pessoa escreve: "Banco do Brasil", "Nubank".</param>
/// <param name="Agencia">Agência.</param>
/// <param name="Conta">Conta, com o dígito.</param>
/// <param name="TipoDeConta">"Corrente" ou "Poupança" — texto, porque a lista do banco é dele.</param>
/// <param name="Titular">Nome do titular da conta.</param>
public sealed record DadosBancarios(string Banco, string Agencia, string Conta, string TipoDeConta, string Titular);

/// <summary>Com quem o formando fala para pagar em espécie, e onde.</summary>
/// <remarks>
/// O nome é obrigatório: é o contrapeso de anunciar dinheiro na tela (P2 de 21/09/2026). Dinheiro é o
/// único meio sem rastro nenhum, e ao menos fica registrado a quem a turma mandou entregar.
/// </remarks>
/// <param name="Nome">Quem recebe — a tesoureira, o presidente.</param>
/// <param name="Onde">Onde encontrar essa pessoa. Opcional.</param>
public sealed record DinheiroComAlguem(string Nome, string? Onde);

/// <summary>
/// Os meios que a turma aceita. Grupo nulo é meio desligado.
/// </summary>
/// <remarks>
/// Não há interruptor separado do dado: o meio existe quando o que ele precisa mostrar está
/// preenchido. Um <c>bool</c> ao lado dos campos seria um segundo estado a manter de acordo com o
/// primeiro, e o dia em que discordassem a tela mostraria uma conta vazia ao formando.
/// </remarks>
/// <param name="Pix">A chave da comissão.</param>
/// <param name="Transferencia">Os dados bancários.</param>
/// <param name="Dinheiro">Com quem falar.</param>
public sealed record MeiosDaConta(ChavePixDaConta? Pix, DadosBancarios? Transferencia, DinheiroComAlguem? Dinheiro)
{
    /// <summary>Os meios ligados, na ordem em que a tela do formando os oferece.</summary>
    public IReadOnlyList<MeioDeRecebimento> Habilitados
    {
        get
        {
            var meios = new List<MeioDeRecebimento>(3);

            if (Pix is not null)
                meios.Add(MeioDeRecebimento.Pix);

            if (Transferencia is not null)
                meios.Add(MeioDeRecebimento.Transferencia);

            if (Dinheiro is not null)
                meios.Add(MeioDeRecebimento.Dinheiro);

            return meios;
        }
    }
}

/// <summary>A conta de recebimento gravada.</summary>
/// <param name="Meios">Os meios que a turma aceita.</param>
/// <param name="AtualizadaEm">Última gravação, em UTC.</param>
/// <param name="ConferidaEm">Quando o Presidente confirmou o titular do PIX, em UTC. Nulo: não conferida.</param>
/// <param name="ConferidaPor">Nome de quem confirmou.</param>
public sealed record ContaDeRecebimentoDetalhe(MeiosDaConta Meios, DateTime AtualizadaEm, DateTime? ConferidaEm, string? ConferidaPor);

/// <summary>A conta da turma, se a comissão já cadastrou. Ausente, a tela abre o formulário.</summary>
/// <param name="Conta">A conta gravada, ou nulo.</param>
public sealed record ContaDeRecebimentoDaTurma(ContaDeRecebimentoDetalhe? Conta);

/// <summary>O PIX de R$ 1,00 que o Presidente paga para ver o titular que o banco mostra.</summary>
/// <param name="CopiaECola">O BR Code, que a tela transforma em QR.</param>
/// <param name="ValorEmCentavos">Valor do teste.</param>
public sealed record PixDeTeste(string CopiaECola, long ValorEmCentavos);
