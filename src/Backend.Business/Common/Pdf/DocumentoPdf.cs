using System.Globalization;
using System.Text;

namespace Backend.Business.Common.Pdf;

/// <summary>Orientação da folha.</summary>
public enum OrientacaoDaPagina
{
    /// <summary>A4 em pé — texto corrido, termo, balancete.</summary>
    Retrato,

    /// <summary>A4 deitado — lista larga, de seis colunas para cima.</summary>
    Paisagem,
}

/// <summary>
/// Uma cor do documento, nos componentes que o PDF usa.
/// </summary>
/// <remarks>
/// Os tons são os mesmos tokens do front (<c>styles/index.css</c>), convertidos de hexadecimal uma
/// vez em <see cref="De"/> — é o que faz o relatório impresso e a tela terem a mesma cara.
/// </remarks>
/// <param name="R">Vermelho, de 0 a 1.</param>
/// <param name="G">Verde, de 0 a 1.</param>
/// <param name="B">Azul, de 0 a 1.</param>
public readonly record struct CorDoPdf(float R, float G, float B)
{
    /// <summary>A cor a partir dos componentes de 0 a 255, como o token do front os escreve.</summary>
    /// <param name="r">Vermelho.</param>
    /// <param name="g">Verde.</param>
    /// <param name="b">Azul.</param>
    public static CorDoPdf De(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    /// <summary>Os três componentes como o operador do PDF os espera.</summary>
    public string Componentes =>
        FormattableString.Invariant(
            $"{R.ToString("0.###", CultureInfo.InvariantCulture)} {G.ToString("0.###", CultureInfo.InvariantCulture)} {B.ToString("0.###", CultureInfo.InvariantCulture)}"
        );
}

/// <summary>
/// PDF de texto corrido: capa, seções, parágrafos, itens e tabelas, com quebra de linha e de página,
/// cabeçalho de tabela repetido e rodapé numerado.
/// </summary>
/// <remarks>
/// Escrito à mão, sem biblioteca. As alternativas eram um pacote novo (QuestPDF e companhia) ou o
/// SkiaSharp que já está no projeto — que no container Alpine não tem fonte instalada e desenharia
/// o texto em branco. Aqui o texto usa as fontes-padrão do PDF (Helvetica), que todo leitor já traz:
/// nada é embutido, e o arquivo sai com poucos KB.
/// <para>
/// <b>Determinístico:</b> sem data de criação nem identificador aleatório — a mesma entrada produz os
/// mesmos bytes, hoje e daqui a um ano. É o que o termo de adesão precisa.
/// </para>
/// <para>
/// <b>Nada é cortado.</b> As larguras de coluna são <b>proporções</b>, não medidas: a tabela sempre é
/// redistribuída para a largura útil da folha, e a célula que não cabe na coluna quebra em linhas.
/// Foi a origem do relatório com a última coluna fora do papel — as larguras somavam 587pt numa
/// folha de 483pt úteis.
/// </para>
/// <para>
/// <c>ponytail:</c> o texto vai em WinAnsi (Latin-1 mais aspas e travessões), que cobre o português;
/// caractere fora dele sai como <c>?</c>. Imagem, fonte embutida e texto em outra escrita pedem uma
/// biblioteca de verdade — troque esta classe quando o primeiro desses aparecer.
/// </para>
/// </remarks>
/// <param name="orientacao">Em pé (o padrão) ou deitada.</param>
public sealed class DocumentoPdf(OrientacaoDaPagina orientacao = OrientacaoDaPagina.Retrato)
{
    private const float LadoMenor = 595;
    private const float LadoMaior = 842;
    private const float Margem = 42;
    private const float CorpoDoTexto = 9.5f;
    private const float Entrelinha = 1.4f;

    /// <summary>Respiro horizontal dentro da célula, somado dos dois lados.</summary>
    private const float RespiroDaCelula = 10;

    /// <summary>Respiro vertical de uma linha de tabela, acima e abaixo do texto.</summary>
    private const float RespiroDaLinha = 5;

    /// <summary>Tinta do texto (<c>--text-primary</c>).</summary>
    private static readonly CorDoPdf Tinta = CorDoPdf.De(0x1A, 0x1A, 0x18);

    /// <summary>Texto de apoio (<c>--text-secondary</c>).</summary>
    private static readonly CorDoPdf Apoio = CorDoPdf.De(0x6B, 0x6B, 0x66);

    /// <summary>Registro técnico e rodapé (<c>--text-muted</c>).</summary>
    private static readonly CorDoPdf Discreta = CorDoPdf.De(0x9A, 0x9A, 0x94);

    /// <summary>O laranja da marca (<c>--brand</c>) — o fio da capa.</summary>
    private static readonly CorDoPdf Marca = CorDoPdf.De(0xF2, 0x99, 0x4A);

    /// <summary>Laranja escuro (<c>--brand-text</c>) — título e cabeçalho de tabela.</summary>
    private static readonly CorDoPdf MarcaEscrita = CorDoPdf.De(0xA6, 0x54, 0x0A);

    /// <summary>Laranja lavado (<c>--brand-wash</c>) — fundo do cabeçalho de tabela.</summary>
    private static readonly CorDoPdf MarcaLavada = CorDoPdf.De(0xFD, 0xF4, 0xEA);

    /// <summary>Laranja suave (<c>--brand-soft</c>) — o fio sob o cabeçalho.</summary>
    private static readonly CorDoPdf MarcaSuave = CorDoPdf.De(0xF6, 0xC8, 0x8E);

    /// <summary>Fundo das linhas pares da tabela (<c>--page</c>).</summary>
    private static readonly CorDoPdf FaixaAlternada = CorDoPdf.De(0xF7, 0xF6, 0xF3);

    /// <summary>Fio de separação (<c>--line</c>).</summary>
    private static readonly CorDoPdf Linha = CorDoPdf.De(0xE8, 0xE7, 0xE3);

    /// <summary>
    /// Larguras da Helvetica, em milésimos do corpo, dos caracteres 32 a 126 — as métricas-padrão da
    /// fonte. Letra acentuada mede como a letra-base. Servem só para decidir onde quebrar a linha.
    /// </summary>
    private static readonly short[] Larguras =
    [
        278,
        278,
        355,
        556,
        556,
        889,
        667,
        191,
        333,
        333,
        389,
        584,
        278,
        333,
        278,
        278,
        556,
        556,
        556,
        556,
        556,
        556,
        556,
        556,
        556,
        556,
        278,
        278,
        584,
        584,
        584,
        556,
        1015,
        667,
        667,
        722,
        722,
        667,
        611,
        778,
        722,
        278,
        500,
        667,
        556,
        833,
        722,
        778,
        667,
        778,
        722,
        667,
        611,
        722,
        667,
        944,
        667,
        667,
        611,
        278,
        278,
        278,
        469,
        556,
        333,
        556,
        556,
        500,
        556,
        556,
        278,
        556,
        556,
        222,
        222,
        500,
        222,
        833,
        556,
        556,
        556,
        556,
        333,
        500,
        278,
        556,
        500,
        722,
        500,
        500,
        500,
        334,
        260,
        334,
        584,
    ];

    /// <summary>Os caracteres de WinAnsi fora do Latin-1, com o código de cada um.</summary>
    private static readonly Dictionary<char, byte> ForaDoLatin1 = new()
    {
        ['€'] = 0x80,
        ['‚'] = 0x82,
        ['„'] = 0x84,
        ['…'] = 0x85,
        ['‘'] = 0x91,
        ['’'] = 0x92,
        ['“'] = 0x93,
        ['”'] = 0x94,
        ['•'] = 0x95,
        ['–'] = 0x96,
        ['—'] = 0x97,
    };

    private readonly List<StringBuilder> _paginas = [];
    private readonly float _largura = orientacao == OrientacaoDaPagina.Retrato ? LadoMenor : LadoMaior;
    private readonly float _altura = orientacao == OrientacaoDaPagina.Retrato ? LadoMaior : LadoMenor;
    private float _y;

    /// <summary>Largura disponível entre as margens.</summary>
    private float LarguraUtil => _largura - (2 * Margem);

    /// <summary>Primeira linha de texto da página.</summary>
    private float TopoDoTexto => _altura - Margem;

    /// <summary>Limite de baixo: abaixo dele começa o rodapé.</summary>
    private static float BaseDoTexto => Margem + 24;

    /// <summary>
    /// A abertura padrão de todo relatório: título, linha da marca e a identificação do documento.
    /// </summary>
    /// <remarks>
    /// É o componente que faz os quatro relatórios e o termo abrirem iguais. Quem precisa de mais —
    /// o balancete, que tem resumo e três quadros — continua com <see cref="Secao"/> daqui para
    /// baixo, mas a primeira tela do arquivo é sempre esta.
    /// </remarks>
    /// <param name="titulo">Nome do relatório.</param>
    /// <param name="subtitulo">A linha embaixo dele — turma e período.</param>
    /// <param name="registro">Terceira linha, menor e discreta: emissor, emissão, aviso. Nula, não sai.</param>
    public DocumentoPdf Capa(string titulo, string subtitulo, string? registro = null)
    {
        Titulo(titulo);
        Paragrafo(subtitulo, cor: Apoio);

        if (!string.IsNullOrWhiteSpace(registro))
            Paragrafo(registro, discreto: true);

        return this;
    }

    /// <summary>Título do documento, grande, no laranja da marca e sobre o fio dela.</summary>
    /// <param name="texto">Texto do título.</param>
    public DocumentoPdf Titulo(string texto)
    {
        Bloco(texto, 18, negrito: true, antes: 0, depois: 13, cor: MarcaEscrita);
        Fio(_y + 4, Marca, espessura: 2.5f, ate: Margem + 64);
        _y -= 10;

        return this;
    }

    /// <summary>Título de seção.</summary>
    /// <param name="texto">Texto do título.</param>
    public DocumentoPdf Secao(string texto) => Bloco(texto, 12, negrito: true, antes: 16, depois: 5, cor: MarcaEscrita);

    /// <summary>Parágrafo, quebrado na largura da página.</summary>
    /// <param name="texto">Texto do parágrafo.</param>
    /// <param name="negrito">Se o parágrafo inteiro vai em negrito.</param>
    /// <param name="discreto">Menor e em cinza claro — para registro técnico, como o User-Agent.</param>
    /// <param name="cor">Tinta do parágrafo; ausente, a do corpo do texto.</param>
    public DocumentoPdf Paragrafo(string texto, bool negrito = false, bool discreto = false, CorDoPdf? cor = null) =>
        Bloco(texto, discreto ? 8 : CorpoDoTexto, negrito, antes: 0, depois: 6, recuo: 0, cor: cor ?? (discreto ? Discreta : Tinta));

    /// <summary>Item de lista, com marcador e recuo.</summary>
    /// <param name="texto">Texto do item.</param>
    public DocumentoPdf Item(string texto)
    {
        var linhas = Quebrar(texto, CorpoDoTexto, negrito: false, LarguraUtil - 14);

        for (var i = 0; i < linhas.Count; i++)
        {
            Avancar(CorpoDoTexto * Entrelinha);

            if (i == 0)
                Escrever(Margem + 2, _y, "•", CorpoDoTexto, negrito: false, cor: Marca);

            Escrever(Margem + 14, _y, linhas[i], CorpoDoTexto, negrito: false, cor: Tinta);
        }

        _y -= 3;

        return this;
    }

    /// <summary>
    /// Tabela com cabeçalho na cor da marca, linhas alternadas e nada cortado.
    /// </summary>
    /// <remarks>
    /// As larguras informadas são <b>proporções</b>, e não pontos: a soma delas é redistribuída na
    /// largura útil da folha, de modo que a tabela nunca sai do papel, seja qual for a orientação.
    /// Célula que não cabe na coluna <b>quebra em linhas</b> em vez de virar reticências — a linha
    /// cresce até caber, e é o fim do nome de fornecedor que terminava em "…".
    /// <para>
    /// A linha nunca é partida ao meio pela quebra de página, e o cabeçalho é <b>redesenhado</b> no
    /// alto da folha seguinte: a página 2 de uma lista de parcelas sem cabeçalho é uma página de
    /// números sem nome.
    /// </para>
    /// </remarks>
    /// <param name="colunas">Proporção de cada coluna e se o texto encosta à direita (valores).</param>
    /// <param name="cabecalho">Títulos das colunas.</param>
    /// <param name="linhas">Registros.</param>
    public DocumentoPdf Tabela(IReadOnlyList<(float Largura, bool Direita)> colunas, string[] cabecalho, IEnumerable<string[]> linhas)
    {
        var larguras = Distribuir(colunas, cabecalho);
        var alternada = false;

        CabecalhoDaTabela(colunas, larguras, cabecalho);

        foreach (var linha in linhas)
        {
            var celulas = new List<string>[colunas.Count];
            var linhasDaCelula = 1;

            for (var i = 0; i < colunas.Count; i++)
            {
                celulas[i] = Quebrar(i < linha.Length ? linha[i] : string.Empty, CorpoDoTexto, negrito: false, larguras[i] - RespiroDaCelula);
                linhasDaCelula = Math.Max(linhasDaCelula, celulas[i].Count);
            }

            var altura = (linhasDaCelula * CorpoDoTexto * Entrelinha) + RespiroDaLinha;

            if (!Cabe(altura))
            {
                NovaPagina();
                CabecalhoDaTabela(colunas, larguras, cabecalho);
            }

            if (alternada)
                Retangulo(Margem, _y - altura, LarguraUtil, altura, FaixaAlternada);

            var topo = _y - (CorpoDoTexto * Entrelinha);
            var x = Margem;

            for (var i = 0; i < colunas.Count; i++)
            {
                for (var l = 0; l < celulas[i].Count; l++)
                    EscreverNaCelula(x, topo - (l * CorpoDoTexto * Entrelinha), larguras[i], celulas[i][l], colunas[i].Direita, negrito: false);

                x += larguras[i];
            }

            _y -= altura;
            alternada = !alternada;
        }

        Fio(_y, Linha);
        _y -= 8;

        return this;
    }

    /// <summary>Espaço vertical em branco.</summary>
    /// <param name="pontos">Altura do espaço.</param>
    public DocumentoPdf Espaco(float pontos = 8)
    {
        _y -= pontos;

        return this;
    }

    /// <summary>Monta o arquivo.</summary>
    /// <param name="rodape">Texto à esquerda do rodapé de toda página; à direita vai "Página x de n".</param>
    public byte[] Gerar(string rodape)
    {
        if (_paginas.Count == 0)
            NovaPagina();

        for (var i = 0; i < _paginas.Count; i++)
        {
            var pagina = _paginas[i];
            var numeracao = $"Página {i + 1} de {_paginas.Count}";

            Fio(Margem + 12, Linha, pagina: pagina);
            Escrever(Margem, Margem, rodape, 7.5f, negrito: false, pagina, Discreta);
            Escrever(_largura - Margem - Medir(numeracao, 7.5f, negrito: false), Margem, numeracao, 7.5f, negrito: false, pagina, Discreta);
        }

        return Montar();
    }

    /// <summary>
    /// As proporções informadas convertidas em pontos, somando exatamente a largura útil.
    /// </summary>
    /// <remarks>
    /// É esta conta que torna a largura da coluna uma intenção ("a descrição é o dobro da data") em
    /// vez de uma medida que precisa bater com o tamanho do papel — e que, quando não batia, punha a
    /// última coluna fora dele.
    /// <para>
    /// <b>Nenhuma coluna fica mais estreita que o próprio título.</b> A coluna que ficaria é travada
    /// na largura do cabeçalho e as irmãs redividem o que sobra, uma por passada — sem isso,
    /// "Competência" quebrava em "Competênci" e um "a" sozinho na linha de baixo. O travamento é
    /// limitado a uma parte igual da folha, para um título comprido não engolir a tabela.
    /// </para>
    /// </remarks>
    /// <param name="colunas">Colunas da tabela.</param>
    /// <param name="cabecalho">Títulos, que definem a largura mínima de cada coluna.</param>
    private float[] Distribuir(IReadOnlyList<(float Largura, bool Direita)> colunas, string[] cabecalho)
    {
        var parteIgual = LarguraUtil / colunas.Count;
        var comPeso = colunas.Sum(coluna => coluna.Largura) > 0;

        var minimos = new float[colunas.Count];
        var pesos = new float[colunas.Count];
        var larguras = new float[colunas.Count];
        var travadas = new bool[colunas.Count];

        for (var i = 0; i < colunas.Count; i++)
        {
            var titulo = i < cabecalho.Length ? cabecalho[i] : string.Empty;

            minimos[i] = Math.Min(parteIgual, Medir(titulo, CorpoDoTexto, negrito: true) + RespiroDaCelula);
            pesos[i] = comPeso ? colunas[i].Largura : 1;
        }

        for (var passada = 0; passada < colunas.Count; passada++)
        {
            var sobra = LarguraUtil;
            var peso = 0f;

            for (var i = 0; i < colunas.Count; i++)
                if (travadas[i])
                    sobra -= larguras[i];
                else
                    peso += pesos[i];

            var estreita = -1;

            for (var i = 0; i < colunas.Count; i++)
            {
                if (travadas[i])
                    continue;

                larguras[i] = peso > 0 ? pesos[i] / peso * sobra : sobra;

                if (larguras[i] < minimos[i] && (estreita < 0 || minimos[i] - larguras[i] > minimos[estreita] - larguras[estreita]))
                    estreita = i;
            }

            if (estreita < 0)
                break;

            larguras[estreita] = minimos[estreita];
            travadas[estreita] = true;
        }

        return larguras;
    }

    /// <summary>O cabeçalho da tabela: faixa lavada, texto na cor da marca e o fio embaixo.</summary>
    /// <param name="colunas">Colunas da tabela.</param>
    /// <param name="larguras">Largura já distribuída de cada uma.</param>
    /// <param name="cabecalho">Títulos.</param>
    private void CabecalhoDaTabela(IReadOnlyList<(float Largura, bool Direita)> colunas, float[] larguras, string[] cabecalho)
    {
        var celulas = new List<string>[colunas.Count];
        var linhasDaCelula = 1;

        for (var i = 0; i < colunas.Count; i++)
        {
            celulas[i] = Quebrar(i < cabecalho.Length ? cabecalho[i] : string.Empty, CorpoDoTexto, negrito: true, larguras[i] - RespiroDaCelula);
            linhasDaCelula = Math.Max(linhasDaCelula, celulas[i].Count);
        }

        // O cabeçalho precisa caber com pelo menos uma linha de dados embaixo: cabeçalho sozinho no pé
        // da página é o título de uma tabela que começa só na folha seguinte.
        var altura = (linhasDaCelula * CorpoDoTexto * Entrelinha) + RespiroDaLinha;

        if (!Cabe(altura + (CorpoDoTexto * Entrelinha) + RespiroDaLinha))
            NovaPagina();

        Retangulo(Margem, _y - altura, LarguraUtil, altura, MarcaLavada);

        var topo = _y - (CorpoDoTexto * Entrelinha);
        var x = Margem;

        for (var i = 0; i < colunas.Count; i++)
        {
            for (var l = 0; l < celulas[i].Count; l++)
                EscreverNaCelula(
                    x,
                    topo - (l * CorpoDoTexto * Entrelinha),
                    larguras[i],
                    celulas[i][l],
                    colunas[i].Direita,
                    negrito: true,
                    MarcaEscrita
                );

            x += larguras[i];
        }

        _y -= altura;
        Fio(_y, MarcaSuave);
    }

    /// <summary>Uma linha de texto dentro de uma célula, encostada à esquerda ou à direita.</summary>
    /// <param name="x">Começo da coluna.</param>
    /// <param name="y">Linha de base do texto.</param>
    /// <param name="largura">Largura da coluna.</param>
    /// <param name="texto">Conteúdo já quebrado.</param>
    /// <param name="direita">Se encosta à direita.</param>
    /// <param name="negrito">Se vai em negrito.</param>
    /// <param name="cor">Tinta; ausente, a do corpo do texto.</param>
    private void EscreverNaCelula(float x, float y, float largura, string texto, bool direita, bool negrito, CorDoPdf? cor = null)
    {
        var respiro = RespiroDaCelula / 2;
        var inicio = direita ? x + largura - respiro - Medir(texto, CorpoDoTexto, negrito) : x + respiro;

        Escrever(inicio, y, texto, CorpoDoTexto, negrito, cor: cor ?? Tinta);
    }

    private DocumentoPdf Bloco(string texto, float corpo, bool negrito, float antes, float depois, float recuo = 0, CorDoPdf? cor = null)
    {
        _y -= antes;

        foreach (var linha in Quebrar(texto, corpo, negrito, LarguraUtil - recuo))
        {
            Avancar(corpo * Entrelinha);
            Escrever(Margem + recuo, _y, linha, corpo, negrito, cor: cor ?? Tinta);
        }

        _y -= depois;

        return this;
    }

    /// <summary>Se ainda há espaço na página corrente para um bloco desta altura.</summary>
    /// <param name="altura">Altura pretendida.</param>
    private bool Cabe(float altura) => _paginas.Count > 0 && _y - altura >= BaseDoTexto;

    /// <summary>Desce o cursor; sem espaço na página, abre outra.</summary>
    /// <param name="altura">Altura da linha.</param>
    private void Avancar(float altura)
    {
        if (!Cabe(altura))
            NovaPagina();

        _y -= altura;
    }

    private void NovaPagina()
    {
        _paginas.Add(new StringBuilder());
        _y = TopoDoTexto;
    }

    private void Escrever(float x, float y, string texto, float corpo, bool negrito, StringBuilder? pagina = null, CorDoPdf? cor = null)
    {
        if (texto.Length == 0)
            return;

        var destino = pagina ?? _paginas[^1];

        destino.Append(CultureInfo.InvariantCulture, $"{(cor ?? Tinta).Componentes} rg BT /{(negrito ? "F2" : "F1")} {Numero(corpo)} Tf ");
        destino.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {Numero(x)} {Numero(y)} Tm ({Codificar(texto)}) Tj ET\n");
    }

    /// <summary>Retângulo cheio — a faixa do cabeçalho e a das linhas alternadas.</summary>
    /// <param name="x">Canto esquerdo.</param>
    /// <param name="y">Canto de baixo.</param>
    /// <param name="largura">Largura.</param>
    /// <param name="altura">Altura.</param>
    /// <param name="cor">Preenchimento.</param>
    private void Retangulo(float x, float y, float largura, float altura, CorDoPdf cor) =>
        _paginas[^1].Append(CultureInfo.InvariantCulture, $"{cor.Componentes} rg {Numero(x)} {Numero(y)} {Numero(largura)} {Numero(altura)} re f\n");

    /// <summary>Fio horizontal na altura informada.</summary>
    /// <param name="y">Altura.</param>
    /// <param name="cor">Cor do traço.</param>
    /// <param name="espessura">Espessura em pontos.</param>
    /// <param name="ate">Onde o fio termina; ausente, a margem direita.</param>
    /// <param name="pagina">Página de destino; ausente, a corrente.</param>
    private void Fio(float y, CorDoPdf cor, float espessura = 0.6f, float? ate = null, StringBuilder? pagina = null) =>
        (pagina ?? _paginas[^1]).Append(
            CultureInfo.InvariantCulture,
            $"{cor.Componentes} RG {Numero(espessura)} w {Numero(Margem)} {Numero(y)} m {Numero(ate ?? (_largura - Margem))} {Numero(y)} l S\n"
        );

    /// <summary>Quebra o texto em linhas que cabem na largura, respeitando as quebras que ele já tem.</summary>
    private static List<string> Quebrar(string texto, float corpo, bool negrito, float largura)
    {
        var linhas = new List<string>();

        foreach (var trecho in texto.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var atual = new StringBuilder();

            foreach (var palavra in trecho.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidata = atual.Length == 0 ? palavra : $"{atual} {palavra}";

                if (Medir(candidata, corpo, negrito) <= largura)
                {
                    atual.Clear().Append(candidata);
                    continue;
                }

                if (atual.Length > 0)
                    linhas.Add(atual.ToString());

                atual.Clear().Append(PartirPalavraLonga(palavra, corpo, negrito, largura, linhas));
            }

            linhas.Add(atual.ToString());
        }

        return linhas;
    }

    /// <summary>Palavra maior que a linha (um hash, uma URL) é partida onde couber; devolve o resto.</summary>
    private static string PartirPalavraLonga(string palavra, float corpo, bool negrito, float largura, List<string> linhas)
    {
        while (Medir(palavra, corpo, negrito) > largura)
        {
            var corte = palavra.Length - 1;
            while (corte > 1 && Medir(palavra[..corte], corpo, negrito) > largura)
                corte--;

            linhas.Add(palavra[..corte]);
            palavra = palavra[corte..];
        }

        return palavra;
    }

    /// <summary>Largura do texto em pontos. O negrito é uns 8% mais largo que a regular.</summary>
    private static float Medir(string texto, float corpo, bool negrito) => texto.Sum(Largura) * corpo / 1000 * (negrito ? 1.08f : 1f);

    private static int Largura(char caractere)
    {
        var letra = caractere is >= ' ' and <= '~' ? caractere : caractere.ToString().Normalize(NormalizationForm.FormD)[0];

        return letra is >= ' ' and <= '~' ? Larguras[letra - ' '] : 556;
    }

    /// <summary>Texto como string literal do PDF: parênteses e barra escapados, o que não é ASCII em octal WinAnsi.</summary>
    private static string Codificar(string texto)
    {
        var saida = new StringBuilder(texto.Length);

        foreach (var caractere in texto)
        {
            switch (caractere)
            {
                case '(' or ')' or '\\':
                    saida.Append('\\').Append(caractere);
                    break;
                case >= ' ' and <= '~':
                    saida.Append(caractere);
                    break;
                default:
                    var codigo = caractere is >= ' ' and <= 'ÿ' ? (byte)caractere : ForaDoLatin1.GetValueOrDefault(caractere, (byte)'?');
                    saida.Append('\\').Append(Convert.ToString(codigo, 8).PadLeft(3, '0'));
                    break;
            }
        }

        return saida.ToString();
    }

    private static string Numero(float valor) => valor.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Os objetos do arquivo: catálogo, árvore de páginas, as duas fontes e, por página, a página e o
    /// conteúdo dela; depois a tabela de posições, que o leitor usa para achar cada objeto.
    /// </summary>
    private byte[] Montar()
    {
        var objetos = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };

        var paginas = new List<int>();

        foreach (var conteudo in _paginas.Select(pagina => pagina.ToString()))
        {
            objetos.Add(
                FormattableString.Invariant(
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Numero(_largura)} {Numero(_altura)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {objetos.Count + 2} 0 R >>"
                )
            );
            paginas.Add(objetos.Count);
            objetos.Add(FormattableString.Invariant($"<< /Length {conteudo.Length} >>\nstream\n{conteudo}endstream"));
        }

        objetos[1] = $"<< /Type /Pages /Kids [{string.Join(' ', paginas.Select(numero => $"{numero} 0 R"))}] /Count {paginas.Count} >>";

        var arquivo = new StringBuilder("%PDF-1.4\n");
        var posicoes = new List<int>();

        for (var i = 0; i < objetos.Count; i++)
        {
            posicoes.Add(arquivo.Length);
            arquivo.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objetos[i]}\nendobj\n");
        }

        var tabela = arquivo.Length;
        arquivo.Append(CultureInfo.InvariantCulture, $"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");

        foreach (var posicao in posicoes)
            arquivo.Append(CultureInfo.InvariantCulture, $"{posicao:D10} 00000 n \n");

        arquivo.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R >>\nstartxref\n{tabela}\n%%EOF\n");

        return Encoding.ASCII.GetBytes(arquivo.ToString());
    }
}
