using Backend.Business.Privacidade.Models;
using Shouldly;

namespace Backend.UnitTests.Privacidade;

/// <summary>
/// As transições do pedido do titular: prazo, confirmação, desistência e disponibilidade do pacote.
///
/// São elas que decidem <b>quando</b> a anonimização acontece, e ela é irreversível — é o lugar do
/// projeto onde um estado errado apaga a vida de alguém na turma antes da hora.
/// </summary>
public sealed class SolicitacaoDePrivacidadeTests
{
    private static readonly Guid Titular = Guid.CreateVersion7();
    private static readonly DateTime Agora = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Exportacao_nasce_vencida_e_sai_na_proxima_passada_do_worker()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exportacao, Titular, Agora);

        pedido.PrazoEm.ShouldBe(Agora);
    }

    /// <summary>Art. 18, §3º: quinze dias. É também a janela de arrependimento.</summary>
    [Fact]
    public void Exclusao_nasce_com_quinze_dias_pela_frente()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exclusao, Titular, Agora);

        pedido.PrazoEm.ShouldBe(Agora.AddDays(15));
        pedido.Status.ShouldBe(StatusDaSolicitacaoDePrivacidade.Pendente);
    }

    [Fact]
    public void Confirmar_antecipa_o_prazo_para_agora()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exclusao, Titular, Agora);

        var confirmacao = pedido.Confirmar(Agora.AddDays(2));

        confirmacao.Sucesso.ShouldBeTrue();
        pedido.PrazoEm.ShouldBe(Agora.AddDays(2));
        pedido.ConfirmadaEm.ShouldBe(Agora.AddDays(2));
    }

    /// <summary>O clique duplo no link do e-mail não é um segundo pedido.</summary>
    [Fact]
    public void Confirmar_duas_vezes_nao_adianta_o_relogio_de_novo()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exclusao, Titular, Agora);

        pedido.Confirmar(Agora.AddDays(2));
        pedido.Confirmar(Agora.AddDays(5));

        pedido.PrazoEm.ShouldBe(Agora.AddDays(2));
    }

    [Fact]
    public void Exportacao_nao_tem_o_que_confirmar()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exportacao, Titular, Agora);

        pedido.Confirmar(Agora).PrimeiroErro.Codigo.ShouldBe("privacidade.nada_a_confirmar");
    }

    [Fact]
    public void Cancelar_encerra_o_pedido_pendente()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exclusao, Titular, Agora);

        pedido.Cancelar(Agora.AddDays(1)).Sucesso.ShouldBeTrue();

        pedido.Status.ShouldBe(StatusDaSolicitacaoDePrivacidade.Cancelada);
        pedido.ConcluidaEm.ShouldBe(Agora.AddDays(1));
    }

    /// <summary>Depois de anonimizar não há o que desfazer — e a porta fecha junto.</summary>
    [Fact]
    public void Cancelar_o_que_ja_foi_atendido_e_recusado()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exclusao, Titular, Agora);
        pedido.ConcluirExclusao(Agora);

        pedido.Cancelar(Agora.AddDays(1)).PrimeiroErro.Codigo.ShouldBe("privacidade.solicitacao_encerrada");
    }

    [Fact]
    public void Cancelado_nao_volta_a_ser_confirmavel()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exclusao, Titular, Agora);
        pedido.Cancelar(Agora);

        pedido.Confirmar(Agora.AddDays(1)).PrimeiroErro.Codigo.ShouldBe("privacidade.solicitacao_encerrada");
    }

    [Fact]
    public void Pacote_fica_disponivel_por_sete_dias()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exportacao, Titular, Agora);
        pedido.ConcluirExportacao(Guid.CreateVersion7(), Agora);

        pedido.Disponivel(Agora.AddDays(6)).ShouldBeTrue();
        pedido.Disponivel(Agora.AddDays(8)).ShouldBeFalse();
    }

    /// <summary>A linha fica, porque é o registro de que a Kapa respondeu. Só os bytes somem.</summary>
    [Fact]
    public void Expirar_tira_o_arquivo_e_mantem_a_solicitacao()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exportacao, Titular, Agora);
        pedido.ConcluirExportacao(Guid.CreateVersion7(), Agora);

        pedido.Expirar();

        pedido.ArquivoId.ShouldBeNull();
        pedido.Status.ShouldBe(StatusDaSolicitacaoDePrivacidade.Concluida);
        pedido.Disponivel(Agora).ShouldBeFalse();
    }

    /// <summary>
    /// Pedido de titular parado em "pendente" para sempre é o que vira reclamação na ANPD: esgotadas
    /// as tentativas, ele precisa mostrar o motivo.
    /// </summary>
    [Fact]
    public void Falha_so_encerra_depois_de_esgotar_as_tentativas()
    {
        var pedido = SolicitacaoDePrivacidade.Nova(TipoDeSolicitacao.Exportacao, Titular, Agora);

        for (var tentativa = 1; tentativa < SolicitacaoDePrivacidade.MaximoDeTentativas; tentativa++)
        {
            pedido.Tentar();
            pedido.Falhar("provedor fora do ar");
            pedido.Status.ShouldBe(StatusDaSolicitacaoDePrivacidade.Pendente);
        }

        pedido.Tentar();
        pedido.Falhar("provedor fora do ar");

        pedido.Status.ShouldBe(StatusDaSolicitacaoDePrivacidade.Falhou);
        pedido.Motivo.ShouldBe("provedor fora do ar");
    }
}
