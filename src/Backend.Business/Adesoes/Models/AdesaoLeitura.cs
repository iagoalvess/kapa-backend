using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Adesoes.Models;

/// <summary>Uma versão do termo, com o texto.</summary>
/// <param name="Id">Identificador da versão.</param>
/// <param name="Versao">Número da versão.</param>
/// <param name="Conteudo">Texto integral, em markdown.</param>
/// <param name="VigenteDesde">Publicação, em UTC.</param>
public sealed record VersaoDoTermo(Guid Id, int Versao, string Conteudo, DateTime VigenteDesde);

/// <summary>Uma versão do termo na lista da comissão.</summary>
/// <param name="Id">Identificador da versão.</param>
/// <param name="Versao">Número da versão.</param>
/// <param name="VigenteDesde">Publicação, em UTC.</param>
/// <param name="Adesoes">Quantos aceitaram esta versão.</param>
public sealed record TermoPublicado(Guid Id, int Versao, DateTime VigenteDesde, int Adesoes);

/// <summary>Texto da versão nova do termo.</summary>
/// <param name="Conteudo">Markdown.</param>
public sealed record PublicarTermo(string Conteudo);

/// <summary>
/// O que a tela de adesão mostra antes do aceite: o termo vigente, o plano vigente e o hash dos dois.
/// </summary>
/// <remarks>Cada parte ausente é o que falta à turma — a tela explica em vez de quebrar.</remarks>
/// <param name="Termo">Versão vigente, se a comissão já publicou.</param>
/// <param name="Plano">Plano vigente congelado como seria aceito agora, se a turma já tem.</param>
/// <param name="HashDoConteudo">Hash que o aceite devolve. Só existe com os dois presentes.</param>
/// <param name="Resumo">
/// Resumo do termo vigente gerado por IA (Sprint 24), se já existir. Fora do hash: aparecer depois não
/// invalida o que está na tela.
/// </param>
/// <param name="Catalogo">Os pacotes à venda, de onde o formando monta a cesta (Sprint 47).</param>
/// <param name="CestaContratada">
/// Os pacotes que o formando já contratou numa adesão anterior: a re-adesão a uma versão nova do termo mantém a mesma
/// cesta, e mudá-la é a Sprint 48. Vazia para quem nunca aderiu.
/// </param>
public sealed record ConteudoParaAdesao(
    VersaoDoTermo? Termo,
    SnapshotDoPlano? Plano,
    string? HashDoConteudo,
    string? Resumo,
    IReadOnlyList<PacoteDoCatalogo> Catalogo,
    IReadOnlyList<Guid> CestaContratada
);

/// <summary>Um pacote como o formando o vê na adesão.</summary>
/// <param name="Id">Pacote.</param>
/// <param name="Grupo">Grupo de faixas ("Festa"); nulo é pacote avulso.</param>
/// <param name="Tipo">Categoria.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Preço total.</param>
/// <param name="NumeroDeParcelas">Em quantas parcelas quem adere hoje paga — menos que o item, para quem chega tarde.</param>
/// <param name="ConvitesDaFesta">Convites da festa que o pacote concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que o pacote concede.</param>
public sealed record PacoteDoCatalogo(
    Guid Id,
    string? Grupo,
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int ConvitesDaFesta,
    int ConvitesDaColacao
);

/// <summary>Pedido de aceite.</summary>
/// <param name="HashDoConteudo">O hash do conteúdo que a tela exibiu, como veio de <see cref="ConteudoParaAdesao"/>.</param>
/// <param name="Codigo">Os seis dígitos enviados ao e-mail da conta, pedidos antes do aceite.</param>
/// <param name="Pacotes">A cesta: os pacotes escolhidos no catálogo (Sprint 47). Ao menos um (D33).</param>
/// <param name="Observacoes">O detalhe livre de cada pacote — fora do hash (Sprint 48, D40).</param>
public sealed record AderirAoTermo(
    string HashDoConteudo,
    string Codigo,
    IReadOnlyList<Guid> Pacotes,
    IReadOnlyList<ObservacaoDoPacote>? Observacoes = null
);

/// <summary>Para onde foi o código de confirmação, para a tela dizer em que caixa de entrada olhar.</summary>
/// <param name="Email">E-mail da conta, mascarado — a tela confirma o destino sem expor o endereço inteiro.</param>
/// <param name="ValidoPorMinutos">Por quanto tempo o código vale, para o texto da tela.</param>
public sealed record CodigoEnviado(string Email, int ValidoPorMinutos);

/// <summary>Uma adesão, com o termo e o plano aceitos.</summary>
/// <param name="Id">Identificador da adesão.</param>
/// <param name="Versao">Versão do termo aceita.</param>
/// <param name="AceitoEm">Momento do aceite, em UTC.</param>
/// <param name="HashDoConteudo">Hash gravado.</param>
/// <param name="NomeCompleto">Nome no instante do aceite.</param>
/// <param name="Cpf">CPF no instante do aceite.</param>
/// <param name="EmailDoAceite">E-mail que recebeu o código confirmado; vazio nas adesões anteriores ao código.</param>
/// <param name="ConteudoDoTermo">Markdown da versão aceita.</param>
/// <param name="Plano">Plano aceito.</param>
public sealed record AdesaoDetalhe(
    Guid Id,
    int Versao,
    DateTime AceitoEm,
    string HashDoConteudo,
    string NomeCompleto,
    string Cpf,
    string EmailDoAceite,
    string ConteudoDoTermo,
    SnapshotDoPlano Plano
);

/// <summary>A situação do próprio formando diante do termo.</summary>
/// <param name="Adesao">A adesão mais recente, se houver.</param>
/// <param name="Pendencias">Itens do cadastro que faltam para aderir, pelos nomes de <c>ItensDoCadastro</c>.</param>
/// <param name="MenorDeIdade">Se a data de nascimento informada dá menos de 18 anos hoje.</param>
public sealed record MinhaAdesao(AdesaoDetalhe? Adesao, IReadOnlyList<string> Pendencias, bool MenorDeIdade);

/// <summary>
/// Só o que a guarda de adesão e o ponto do menu perguntam: há termo, há plano, e eu já aderi?
/// </summary>
/// <remarks>
/// Existe para não baixar <see cref="MinhaAdesao"/> e o termo vigente inteiros — texto, plano simulado, catálogo —
/// em toda tela só para decidir para onde a pessoa vai.
/// </remarks>
/// <param name="TermoPublicado">A turma tem versão publicada do termo — é o que o gate da API exige (Sprint 47, D18).</param>
/// <param name="PlanoVigente">Há plano de cobrança vigente: sem ele, o termo ainda não pode ser aceito.</param>
/// <param name="Aderiu">O próprio vínculo aderiu a alguma versão do termo.</param>
public sealed record SituacaoDaMinhaAdesao(bool TermoPublicado, bool PlanoVigente, bool Aderiu);

/// <summary>A adesão gravada, com o texto da versão aceita — o que a leitura e o PDF precisam.</summary>
/// <param name="Adesao">Registro da adesão.</param>
/// <param name="ConteudoDoTermo">Markdown da versão aceita.</param>
public sealed record AdesaoComTermo(AdesaoDoFormando Adesao, string ConteudoDoTermo);

/// <summary>Um membro no painel de adesões.</summary>
/// <param name="UsuarioId">Membro.</param>
/// <param name="Nome">Nome civil, se informado; senão, o da conta.</param>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Papel">Papel na formatura.</param>
/// <param name="AdesaoId">Adesão mais recente, se houver — é o que abre o PDF.</param>
/// <param name="Versao">Versão aceita na adesão mais recente.</param>
/// <param name="AceitoEm">Momento dela, em UTC.</param>
/// <param name="Cesta">Os pacotes da cesta, com o detalhe livre de cada um (Sprint 48, D40).</param>
public sealed record SituacaoDeAdesao(
    Guid UsuarioId,
    string Nome,
    string Email,
    string Papel,
    Guid? AdesaoId,
    int? Versao,
    DateTime? AceitoEm,
    IReadOnlyList<PacoteEscolhido>? Cesta = null
);

/// <summary>Um pacote na cesta de um formando, como a Gestão o lê no painel de adesões (D40).</summary>
/// <param name="ItemDeCobrancaId">Pacote.</param>
/// <param name="Rotulo">"Festa — Festa 15", ou o nome do pacote avulso.</param>
/// <param name="Observacao">O detalhe livre que o formando escreveu, se escreveu.</param>
public sealed record PacoteEscolhido(Guid ItemDeCobrancaId, string Rotulo, string? Observacao);

/// <summary>Filtros do painel de adesões.</summary>
/// <param name="Aderiu">Só quem aderiu (<c>true</c>), só quem falta (<c>false</c>) ou todos (nulo).</param>
/// <param name="Busca">Trecho do nome ou do e-mail.</param>
public sealed record FiltroDeAdesoes(bool? Aderiu = null, string? Busca = null);

/// <summary>O número que a comissão olha toda semana: quantos aderiram, de quantos.</summary>
/// <param name="Membros">Membros ativos da turma.</param>
/// <param name="Aderiram">Membros ativos com alguma adesão.</param>
public sealed record ResumoDeAdesoes(int Membros, int Aderiram);

/// <summary>O termo assinado, em PDF.</summary>
/// <param name="Conteudo">Bytes do arquivo.</param>
/// <param name="NomeDoArquivo">Nome sugerido para o download.</param>
public sealed record PdfDaAdesao(byte[] Conteudo, string NomeDoArquivo);

/// <summary>Uma versão do termo que o job de resumo ainda precisa processar.</summary>
/// <param name="FormaturaId">Turma dona — o escopo do worker é apontado para ela antes de gerar.</param>
/// <param name="TermoId">Versão.</param>
public sealed record TermoSemResumo(Guid FormaturaId, Guid TermoId);
