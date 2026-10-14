using Backend.Business.Abstractions;
using Backend.Business.Legal.Models;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Services;

namespace Backend.Worker.Comandos;

/// <summary>Atendimento interno de pedidos recebidos pelo suporte, sem iniciar os jobs ou expor endpoints.</summary>
public static class ComandoDePrivacidade
{
    /// <summary>Executa exportação, limpeza cadastral ou alteração de marketing para o titular verificado pelo suporte.</summary>
    /// <remarks>A limpeza cadastral exige confirmação explícita e não representa eliminação integral dos dados.</remarks>
    public static async Task<int> Executar(string[] argumentos, IServiceProvider services, TextWriter saida, CancellationToken ct = default)
    {
        if (
            argumentos.Length != 4
            || argumentos[0] != "privacidade"
            || !Guid.TryParse(argumentos[2], out var titular)
            || titular == Guid.Empty
            || argumentos[1] is not ("exportar" or "excluir-cadastro" or "novidades")
            || (argumentos[1] == "excluir-cadastro" && argumentos[3] != "--confirmar")
            || (argumentos[1] == "novidades" && argumentos[3] is not ("receber" or "parar"))
        )
        {
            await saida.WriteLineAsync("Uso: privacidade exportar <uuid-do-titular> <arquivo.zip>");
            await saida.WriteLineAsync("     privacidade excluir-cadastro <uuid-do-titular> --confirmar");
            await saida.WriteLineAsync("     privacidade novidades <uuid-do-titular> receber|parar");
            return 2;
        }

        if (argumentos[1] == "exportar" && !string.Equals(Path.GetExtension(argumentos[3]), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            await saida.WriteLineAsync("Informe um arquivo de destino com extensão .zip.");
            return 2;
        }

        using var escopo = services.CreateScope();
        var servicos = escopo.ServiceProvider;

        if (argumentos[1] == "exportar")
        {
            var destino = Path.GetFullPath(argumentos[3]);
            if (File.Exists(destino))
            {
                await saida.WriteLineAsync("O arquivo já existe. Escolha outro destino; nenhuma cópia foi sobrescrita.");
                return 2;
            }

            var dados = await servicos.GetRequiredService<IPrivacidadeRepository>().ObterMeusDadosDeTodasAsFormaturas(titular, ct);
            if (dados is null)
            {
                await saida.WriteLineAsync("Titular não encontrado. Nenhum arquivo foi gerado.");
                return 1;
            }

            var pacote = PacoteDeDados.Gerar(dados, DateTime.UtcNow);
            // CreateNew também impede sobrescrita se outro processo criar o arquivo após a conferência.
            await using (var arquivo = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await arquivo.WriteAsync(pacote, ct);

            await saida.WriteLineAsync($"Exportação gerada: {destino}");
            await saida.WriteLineAsync(
                "Revise o pacote e complemente arquivos, compras avulsas e registros técnicos conforme o pedido antes da entrega."
            );
            return 0;
        }

        var unitOfWork = servicos.GetRequiredService<IUnitOfWork>();
        var marketing = servicos.GetRequiredService<IComunicacaoDoKapaService>();
        var origem = new OrigemDoAceite(string.Empty, "suporte-interno");
        var resultado = await unitOfWork.EmTransacaoAsync<Result>(
            async token =>
            {
                var preferencia = await marketing.DefinirPreferencia(
                    titular,
                    argumentos[1] == "novidades" && argumentos[3] == "receber",
                    OrigemDoConsentimentoDeMarketing.Suporte,
                    origem,
                    token
                );
                if (preferencia.Falhou || argumentos[1] == "novidades")
                    return preferencia;

                return await servicos.GetRequiredService<IAnonimizacaoDeTitular>().Anonimizar(titular, token);
            },
            ct
        );

        if (resultado.Falhou)
        {
            await saida.WriteLineAsync($"Operação recusada: {resultado.PrimeiroErro.Codigo}.");
            return 1;
        }

        await saida.WriteLineAsync(
            argumentos[1] == "novidades"
                ? "Preferência de marketing registrada conforme o pedido ao suporte."
                : "Conta desativada e cadastro limpo. Registros contratuais/financeiros permanecem; avalie os demais dados antes de encerrar o pedido."
        );
        return 0;
    }
}
