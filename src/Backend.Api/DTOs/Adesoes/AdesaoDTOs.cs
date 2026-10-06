using Backend.Api.DTOs.Cobrancas;
using Backend.Business.Cobrancas.Models;

namespace Backend.Api.DTOs.Adesoes;

/// <summary>Corpo da publicação de uma versão nova do termo.</summary>
/// <param name="Conteudo">Texto integral, em markdown.</param>
public sealed record PublicarTermoRequestDTO(string? Conteudo);

/// <summary>Corpo do aceite.</summary>
/// <param name="HashDoConteudo">O <c>hashDoConteudo</c> recebido em <c>GET /adesoes/termos/vigente</c> — prova de qual texto e qual plano estavam na tela.</param>
/// <param name="Codigo">Os seis dígitos recebidos por e-mail em <c>POST /adesoes/codigo</c>.</param>
/// <param name="Pacotes">
/// A cesta: os ids dos pacotes escolhidos, os mesmos passados em <c>?pacotes=</c> ao pedir o termo (Sprint 47). Ao menos
/// um (400 <c>adesao.cesta_sem_escolha</c>); uma faixa por grupo (400 <c>cobranca.faixa_invalida</c>).
/// </param>
/// <param name="Observacoes">O detalhe livre de cada pacote — "beca M" (Sprint 48, D40). Fora do hash.</param>
public sealed record AderirRequestDTO(
    string? HashDoConteudo,
    string? Codigo,
    IReadOnlyList<Guid>? Pacotes = null,
    IReadOnlyList<ObservacaoDoPacoteDTO>? Observacoes = null
);

/// <summary>O detalhe livre de um pacote da cesta (Sprint 48, D40).</summary>
/// <param name="PacoteId">Pacote.</param>
/// <param name="Texto">O detalhe; até 300 caracteres.</param>
public sealed record ObservacaoDoPacoteDTO(Guid PacoteId, string? Texto);

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
/// <param name="Cesta">O quadro de escolhas: os pacotes contratados e o que concedem. Nulo nas adesões anteriores à cesta.</param>
public sealed record PlanoAceitoDTO(
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    int DiasMinimosParaDesconto,
    IReadOnlyList<ItemAceitoDTO> Itens,
    IReadOnlyList<ParcelaSimuladaDTO> Parcelas,
    long TotalEmCentavos,
    IReadOnlyList<PacoteDaCestaDTO>? Cesta
);

/// <summary>Um pacote do quadro de escolhas, como foi congelado no aceite (Sprint 47).</summary>
/// <param name="ItemId">Pacote de origem.</param>
/// <param name="Grupo">Grupo de faixas ("Festa"); nulo é pacote avulso.</param>
/// <param name="Tipo">Categoria.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Preço total.</param>
/// <param name="ConvitesDaFesta">Convites da festa que o pacote concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que o pacote concede.</param>
public sealed record PacoteDaCestaDTO(
    Guid ItemId,
    string? Grupo,
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int ConvitesDaFesta,
    int ConvitesDaColacao
);

/// <summary>Um pacote do catálogo, como o formando o vê na adesão (Sprint 47).</summary>
/// <param name="Id">Pacote — o id que vai em <c>pacotes</c>.</param>
/// <param name="Grupo">Grupo de faixas; a tela mostra um bloco por grupo e deixa escolher só uma faixa.</param>
/// <param name="Tipo">Categoria.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Preço total.</param>
/// <param name="NumeroDeParcelas">Em quantas parcelas quem adere hoje paga.</param>
/// <param name="ConvitesDaFesta">Convites da festa que o pacote concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que o pacote concede.</param>
public sealed record PacoteDoCatalogoDTO(
    Guid Id,
    string? Grupo,
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int ConvitesDaFesta,
    int ConvitesDaColacao
);

/// <summary>O que a tela de adesão mostra antes do aceite. Parte ausente é o que falta à turma.</summary>
/// <param name="Termo">Versão vigente, se publicada.</param>
/// <param name="Plano">Plano vigente como seria aceito agora, se houver.</param>
/// <param name="HashDoConteudo">Devolvido no aceite. Só vem com termo e plano.</param>
/// <param name="Resumo">
/// Um ou dois parágrafos curtos gerados por IA sobre o termo vigente, se já existirem. Não faz parte do que se aceita:
/// fora do hash, do PDF e do e-mail.
/// </param>
/// <param name="Catalogo">Os pacotes à venda, de onde o formando monta a cesta.</param>
/// <param name="CestaContratada">Os pacotes que o formando já contratou: a re-adesão mantém a mesma cesta. Vazia para quem nunca aderiu.</param>
public sealed record ConteudoParaAdesaoDTO(
    VersaoDoTermoDTO? Termo,
    PlanoAceitoDTO? Plano,
    string? HashDoConteudo,
    string? Resumo,
    IReadOnlyList<PacoteDoCatalogoDTO> Catalogo,
    IReadOnlyList<Guid> CestaContratada
);

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

/// <summary>O que a guarda de adesão e o ponto do menu precisam saber, sem o termo nem a adesão.</summary>
/// <param name="TermoPublicado">A turma publicou o termo — com ele, o formando que não aderiu fica barrado.</param>
/// <param name="PlanoVigente">Há plano de cobrança vigente; sem ele, o termo ainda não pode ser aceito.</param>
/// <param name="Aderiu">O próprio membro aderiu a alguma versão do termo.</param>
public sealed record SituacaoDaMinhaAdesaoDTO(bool TermoPublicado, bool PlanoVigente, bool Aderiu);

/// <summary>Um membro no painel de adesões.</summary>
/// <param name="UsuarioId">Membro.</param>
/// <param name="Nome">Nome civil, ou o da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="AdesaoId">Adesão mais recente, se houver.</param>
/// <param name="Versao">Versão aceita nela.</param>
/// <param name="AceitoEm">Momento dela, em UTC.</param>
/// <param name="Cesta">Os pacotes da cesta, com o detalhe livre de cada um (Sprint 48, D40).</param>
public sealed record SituacaoDeAdesaoDTO(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    Guid? AdesaoId,
    int? Versao,
    DateTime? AceitoEm,
    IReadOnlyList<PacoteEscolhidoDTO> Cesta
);

/// <summary>Um pacote na cesta de um formando, no painel de adesões.</summary>
/// <param name="ItemDeCobrancaId">Pacote.</param>
/// <param name="Rotulo">"Festa — Festa 15", ou o nome do pacote avulso.</param>
/// <param name="Observacao">O detalhe livre que o formando escreveu.</param>
public sealed record PacoteEscolhidoDTO(Guid ItemDeCobrancaId, string Rotulo, string? Observacao);

/// <summary>Quantos aderiram, de quantos.</summary>
/// <param name="Membros">Membros ativos.</param>
/// <param name="Aderiram">Membros ativos com adesão.</param>
public sealed record ResumoDeAdesoesDTO(int Membros, int Aderiram);

/// <summary>Um pacote na cesta do formando (Sprint 48).</summary>
/// <param name="ItemDeCobrancaId">Pacote.</param>
/// <param name="Tipo">Categoria.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="Grupo">Grupo de faixas; nulo é pacote avulso.</param>
/// <param name="ContratadoEmCentavos">O que as parcelas dele somam — o preço que o formando aceitou.</param>
/// <param name="ConvitesDaFesta">Convites da festa que concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que concede.</param>
/// <param name="Observacao">O detalhe livre.</param>
/// <param name="CancelavelAte">Último dia para pedir o cancelamento; nulo, sem trava (D36).</param>
/// <param name="CancelamentoSolicitado">Há solicitação esperando a comissão.</param>
public sealed record PacoteNaCestaDTO(
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    string? Grupo,
    long ContratadoEmCentavos,
    int ConvitesDaFesta,
    int ConvitesDaColacao,
    string? Observacao,
    DateOnly? CancelavelAte,
    bool CancelamentoSolicitado
);

/// <summary>Um pacote que o aditivo pode acrescentar (D38).</summary>
/// <param name="ItemDeCobrancaId">Pacote.</param>
/// <param name="Tipo">Categoria.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="Grupo">Grupo de faixas; nulo é pacote avulso.</param>
/// <param name="ValorEmCentavos">Preço de hoje no catálogo.</param>
/// <param name="DiferencaEmCentavos">O que o aditivo cobraria: o preço menos o já contratado na faixa que sai.</param>
/// <param name="ConvitesDaFesta">Convites da festa que concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que concede.</param>
/// <param name="Substitui">A faixa do mesmo grupo que sai da cesta; nulo quando é pacote novo.</param>
public sealed record PacoteDisponivelDTO(
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    string? Grupo,
    long ValorEmCentavos,
    long DiferencaEmCentavos,
    int ConvitesDaFesta,
    int ConvitesDaColacao,
    Guid? Substitui
);

/// <summary>A cesta do formando e o que ele ainda pode acrescentar.</summary>
/// <param name="Pacotes">O que está na cesta hoje.</param>
/// <param name="Disponiveis">Faixas acima no mesmo grupo e pacotes novos.</param>
public sealed record MinhaCestaDTO(IReadOnlyList<PacoteNaCestaDTO> Pacotes, IReadOnlyList<PacoteDisponivelDTO> Disponiveis);

/// <summary>Corpo da prévia do aditivo.</summary>
/// <param name="Pacotes">Os pacotes que entram.</param>
public sealed record SimularAditivoRequestDTO(IReadOnlyList<Guid>? Pacotes);

/// <summary>Corpo do aceite do aditivo — o rito da adesão (D38).</summary>
/// <param name="Pacotes">Os pacotes que entram, os mesmos da prévia.</param>
/// <param name="HashDoConteudo">O hash da prévia; se o catálogo mudou, 409 <c>adesao.aditivo_desatualizado</c>.</param>
/// <param name="Codigo">Os seis dígitos de <c>POST /adesoes/aditivo/codigo</c>.</param>
/// <param name="Observacoes">O detalhe livre de cada pacote novo.</param>
public sealed record AceitarAditivoRequestDTO(
    IReadOnlyList<Guid>? Pacotes,
    string? HashDoConteudo,
    string? Codigo,
    IReadOnlyList<ObservacaoDoPacoteDTO>? Observacoes = null
);

/// <summary>Um pacote que entra pelo aditivo.</summary>
/// <param name="Entra">O pacote novo.</param>
/// <param name="Sai">A faixa que ele substitui; nula quando é pacote novo.</param>
/// <param name="JaContratadoEmCentavos">O que a faixa que sai já soma — o que não se cobra de novo.</param>
/// <param name="DiferencaEmCentavos">O que o aditivo cobra por ele.</param>
public sealed record MudancaDaCestaDTO(PacoteDaCestaDTO Entra, PacoteDaCestaDTO? Sai, long JaContratadoEmCentavos, long DiferencaEmCentavos);

/// <summary>O aditivo como o formando o lê antes de aceitar.</summary>
/// <param name="Mudancas">Um pacote que entra por linha.</param>
/// <param name="Parcelas">As parcelas novas, por vencimento.</param>
/// <param name="TotalEmCentavos">O que o formando passa a dever a mais.</param>
/// <param name="HashDoConteudo">Devolvido no aceite.</param>
public sealed record PreviaDoAditivoDTO(
    IReadOnlyList<MudancaDaCestaDTO> Mudancas,
    IReadOnlyList<ParcelaSimuladaDTO> Parcelas,
    long TotalEmCentavos,
    string HashDoConteudo
);
