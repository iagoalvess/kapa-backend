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
public sealed record ConteudoParaAdesao(VersaoDoTermo? Termo, SnapshotDoPlano? Plano, string? HashDoConteudo, string? Resumo);

/// <summary>Pedido de aceite.</summary>
/// <param name="HashDoConteudo">O hash do conteúdo que a tela exibiu, como veio de <see cref="ConteudoParaAdesao"/>.</param>
/// <param name="Codigo">Os seis dígitos enviados ao e-mail da conta, pedidos antes do aceite.</param>
public sealed record AderirAoTermo(string HashDoConteudo, string Codigo);

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
public sealed record SituacaoDeAdesao(Guid UsuarioId, string Nome, string Email, string Papel, Guid? AdesaoId, int? Versao, DateTime? AceitoEm);

/// <summary>Filtros do painel de adesões.</summary>
/// <param name="Aderiu">Só quem aderiu (<c>true</c>), só quem falta (<c>false</c>) ou todos (nulo).</param>
/// <param name="Busca">Trecho do nome ou do e-mail.</param>
public sealed record FiltroDeAdesoes(bool? Aderiu = null, string? Busca = null);

/// <summary>O número que a comissão olha toda semana: quantos aderiram, de quantos.</summary>
/// <param name="Membros">Membros ativos da turma.</param>
/// <param name="Aderiram">Membros ativos com alguma adesão.</param>
/// <param name="VersaoVigente">Versão vigente do termo, se já publicado.</param>
public sealed record ResumoDeAdesoes(int Membros, int Aderiram, int? VersaoVigente);

/// <summary>O termo assinado, em PDF.</summary>
/// <param name="Conteudo">Bytes do arquivo.</param>
/// <param name="NomeDoArquivo">Nome sugerido para o download.</param>
public sealed record PdfDaAdesao(byte[] Conteudo, string NomeDoArquivo);

/// <summary>Uma versão do termo que o job de resumo ainda precisa processar.</summary>
/// <param name="FormaturaId">Turma dona — o escopo do worker é apontado para ela antes de gerar.</param>
/// <param name="TermoId">Versão.</param>
public sealed record TermoSemResumo(Guid FormaturaId, Guid TermoId);
