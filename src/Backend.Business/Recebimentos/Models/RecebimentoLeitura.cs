namespace Backend.Business.Recebimentos.Models;

/// <summary>A chave PIX da turma, como a comissão informa.</summary>
/// <param name="TipoDeChave">Tipo da chave.</param>
/// <param name="Chave">Chave, com ou sem máscara.</param>
/// <param name="NomeDoTitular">Nome do titular, como o banco mostra.</param>
/// <param name="Cidade">Cidade do titular.</param>
/// <param name="Banco">
/// Banco da chave, como o app do pagador mostra: "Nubank", "Banco do Brasil". Opcional — é mais uma coisa para o formando
/// conferir antes de pagar, e não entra no BR Code.
/// </param>
public sealed record ChavePixDaConta(TipoDeChavePix TipoDeChave, string Chave, string NomeDoTitular, string Cidade, string? Banco = null);

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

/// <summary>O que a gravação dos meios deu: aplicada na hora, ou esperando o link do e-mail de quem pediu.</summary>
/// <param name="Conta">A conta como está agora — com a troca, se ela já valeu; nula se ainda não há conta.</param>
/// <param name="ConfirmacaoEnviadaPara">O e-mail, mascarado, que recebeu o link; nulo quando a troca já valeu.</param>
public sealed record GravacaoDaConta(ContaDeRecebimentoDetalhe? Conta, string? ConfirmacaoEnviadaPara);

/// <summary>A troca dos meios esperando confirmação — o que viaja assinado no link do e-mail.</summary>
/// <param name="FormaturaId">Turma da troca.</param>
/// <param name="UsuarioId">Quem pediu, e só ele confirma.</param>
/// <param name="Antes">Impressão digital da conta no pedido: gravada de novo desde então, o link morre.</param>
/// <param name="Depois">Os meios pedidos, já normalizados.</param>
public sealed record TrocaDosMeios(Guid FormaturaId, Guid UsuarioId, string Antes, MeiosDaConta Depois);
