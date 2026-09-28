namespace Backend.Business.IA.Models;

/// <summary>Um pedido a um modelo de linguagem: a instrução fixa da feature e o texto a tratar.</summary>
/// <remarks>
/// Só texto na ida, só texto na volta — sem JSON estruturado, sem ferramentas, sem streaming. Quando
/// uma feature precisar de mais, o campo entra aqui e no <c>ClienteDeModelo</c>, e as demais seguem
/// iguais.
/// </remarks>
/// <param name="Instrucao">Mensagem de sistema: o que fazer com o texto. Fixa por feature, sem dado de ninguém.</param>
/// <param name="Texto">O conteúdo a tratar — e é só ele que sai da plataforma além da instrução.</param>
/// <param name="Modelos">Ids em ordem de preferência: o primeiro que responder ganha.</param>
public sealed record PedidoAoModelo(string Instrucao, string Texto, IReadOnlyList<string> Modelos);

/// <summary>O que um modelo devolveu.</summary>
/// <param name="Texto">Resposta, aparada.</param>
/// <param name="Modelo">Id de quem respondeu — para saber a quem culpar.</param>
public sealed record RespostaDoModelo(string Texto, string Modelo);
