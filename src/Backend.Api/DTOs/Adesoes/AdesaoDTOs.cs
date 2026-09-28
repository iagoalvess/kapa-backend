using Backend.Api.DTOs.Cobrancas;
using Backend.Business.Cobrancas.Models;

namespace Backend.Api.DTOs.Adesoes;

/// <summary>Corpo da publicação de uma versão nova do termo.</summary>
/// <param name="Conteudo">Texto integral, em markdown.</param>
public sealed record PublicarTermoRequestDTO(string? Conteudo);

/// <summary>Corpo do aceite.</summary>
/// <param name="HashDoConteudo">O <c>hashDoConteudo</c> recebido em <c>GET /adesoes/termos/vigente</c> — prova de qual texto e qual plano estavam na tela.</param>
/// <param name="Codigo">Os seis dígitos recebidos por e-mail em <c>POST /adesoes/codigo</c>.</param>
public sealed record AderirRequestDTO(string? HashDoConteudo, string? Codigo);

/// <summary>Para onde foi o código de confirmação.</summary>
/// <param name="Email">E-mail da conta, mascarado.</param>
/// <param name="ValidoPorMinutos">Por quanto tempo o código vale.</param>
public sealed record CodigoEnviadoDTO(string Email, int ValidoPorMinutos);

/// <summary>Uma versão do termo, com o texto.</summary>
/// <param name="Id">Identificador da versão.</param>
/// <param name="Versao">Número da versão.</param>
/// <param name="Conteudo">Texto integral, em markdown.</param>
/// <param name="VigenteDesde">Publicação, em UTC.</param>
public sealed record VersaoDoTermoDTO(Guid Id, int Versao, string Conteudo, DateTime VigenteDesde);

/// <summary>Uma versão na lista da comissão.</summary>
/// <param name="Id">Identificador da versão.</param>
/// <param name="Versao">Número da versão.</param>
/// <param name="VigenteDesde">Publicação, em UTC.</param>
/// <param name="Adesoes">Quantos aceitaram esta versão.</param>
public sealed record TermoPublicadoDTO(Guid Id, int Versao, DateTime VigenteDesde, int Adesoes);

/// <summary>Um item do plano, como foi aceito.</summary>
/// <param name="Tipo">O que cobra.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Total por formando, em centavos.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes.</param>
/// <param name="DiaDeVencimento">Dia do vencimento.</param>
/// <param name="PrimeiroMes">Mês do primeiro vencimento, no dia 1.</param>
public sealed record ItemAceitoDTO(
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes
);

/// <summary>O plano congelado: o que o formando aceita (ou aceitou) pagar.</summary>
/// <param name="PercentualDeMulta">Multa, base 10.000.</param>
/// <param name="PercentualDeJurosAoMes">Juros ao mês, base 10.000.</param>
/// <param name="CarenciaEmDias">Dias sem multa nem juros.</param>
/// <param name="PercentualDeDescontoPorAntecipacao">Desconto por antecipação, base 10.000.</param>
/// <param name="DiasMinimosParaDesconto">Dias de antecedência que o desconto exige; zero nos termos assinados antes de 17/09/2026.</param>
/// <param name="Itens">Itens, na ordem do plano.</param>
/// <param name="Parcelas">
/// A grade, por vencimento — a de quem adere hoje. Quem adere depois do começo do plano deve o mesmo
/// total, redividido pelas parcelas que ainda não venceram.
/// </param>
/// <param name="TotalEmCentavos">Soma das parcelas.</param>
public sealed record PlanoAceitoDTO(
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    int DiasMinimosParaDesconto,
    IReadOnlyList<ItemAceitoDTO> Itens,
    IReadOnlyList<ParcelaSimuladaDTO> Parcelas,
    long TotalEmCentavos
);

/// <summary>O que a tela de adesão mostra antes do aceite. Parte ausente é o que falta à turma.</summary>
/// <param name="Termo">Versão vigente, se publicada.</param>
/// <param name="Plano">Plano vigente como seria aceito agora, se houver.</param>
/// <param name="HashDoConteudo">Devolvido no aceite. Só vem com termo e plano.</param>
/// <param name="Resumo">
/// Um ou dois parágrafos curtos gerados por IA sobre o termo vigente, se já existirem. Não faz parte do que se aceita:
/// fora do hash, do PDF e do e-mail.
/// </param>
public sealed record ConteudoParaAdesaoDTO(VersaoDoTermoDTO? Termo, PlanoAceitoDTO? Plano, string? HashDoConteudo, string? Resumo);

/// <summary>Uma adesão, com o termo e o plano aceitos.</summary>
/// <param name="Id">Identificador — o do PDF.</param>
/// <param name="Versao">Versão aceita.</param>
/// <param name="AceitoEm">Momento do aceite, em UTC.</param>
/// <param name="HashDoConteudo">SHA-256 do termo e do plano aceitos.</param>
/// <param name="NomeCompleto">Nome no instante do aceite.</param>
/// <param name="Cpf">CPF no instante do aceite, só os dígitos.</param>
/// <param name="EmailDoAceite">E-mail que recebeu o código confirmado; vazio nas adesões anteriores ao código.</param>
/// <param name="ConteudoDoTermo">Markdown da versão aceita.</param>
/// <param name="Plano">Plano aceito.</param>
public sealed record AdesaoDTO(
    Guid Id,
    int Versao,
    DateTime AceitoEm,
    string HashDoConteudo,
    string NomeCompleto,
    string Cpf,
    string EmailDoAceite,
    string ConteudoDoTermo,
    PlanoAceitoDTO Plano
);

/// <summary>A situação do próprio formando diante do termo.</summary>
/// <param name="Adesao">A adesão mais recente, se houver.</param>
/// <param name="Pendencias">O que falta no cadastro para aderir: <c>nomeCompleto</c>, <c>cpf</c>, <c>dataDeNascimento</c>.</param>
/// <param name="MenorDeIdade">Se a data de nascimento dá menos de 18 anos hoje — aí a adesão é com a comissão.</param>
public sealed record MinhaAdesaoDTO(AdesaoDTO? Adesao, IReadOnlyList<string> Pendencias, bool MenorDeIdade);

/// <summary>Um membro no painel de adesões.</summary>
/// <param name="UsuarioId">Membro.</param>
/// <param name="Nome">Nome civil, ou o da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="AdesaoId">Adesão mais recente, se houver.</param>
/// <param name="Versao">Versão aceita nela.</param>
/// <param name="AceitoEm">Momento dela, em UTC.</param>
public sealed record SituacaoDeAdesaoDTO(Guid UsuarioId, string Nome, string Email, string Papel, Guid? AdesaoId, int? Versao, DateTime? AceitoEm);

/// <summary>Quantos aderiram, de quantos.</summary>
/// <param name="Membros">Membros ativos.</param>
/// <param name="Aderiram">Membros ativos com adesão.</param>
/// <param name="VersaoVigente">Versão vigente do termo, se publicada.</param>
public sealed record ResumoDeAdesoesDTO(int Membros, int Aderiram, int? VersaoVigente);
