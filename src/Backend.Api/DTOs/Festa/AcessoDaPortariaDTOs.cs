using Backend.Business.Festa.Models;

namespace Backend.Api.DTOs.Festa;

/// <summary>Corpo de quem a Gestão libera para a portaria.</summary>
/// <param name="EventoId">Evento da portaria.</param>
/// <param name="Nome">Como a comissão chama a pessoa.</param>
public sealed record AcessoDaPortariaRequestDTO(Guid EventoId, string? Nome);

/// <summary>Corpo da ativação: o segredo do link.</summary>
/// <param name="Token">O que vem depois de <c>/portaria/acesso/</c> no link.</param>
public sealed record AtivacaoDaPortariaRequestDTO(string? Token);

/// <summary>Uma pessoa liberada para a portaria.</summary>
/// <param name="Id">Acesso.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Situacao">Aguardando ou ativo.</param>
/// <param name="Token">O segredo do link, só enquanto ninguém o abriu.</param>
/// <param name="AtivadoEm">Quando o celular foi liberado.</param>
/// <param name="CriadoEm">Quando a Gestão liberou.</param>
public sealed record AcessoDaPortariaDTO(Guid Id, string Nome, SituacaoDoAcesso Situacao, string? Token, DateTime? AtivadoEm, DateTime CriadoEm);

/// <summary>A credencial do celular da portaria.</summary>
/// <param name="AcessoId">Acesso — o "quem validou" das entradas deste celular.</param>
/// <param name="Nome">Nome da pessoa.</param>
/// <param name="Token">Vai no <c>Authorization: Bearer</c> das rotas da portaria.</param>
/// <param name="ExpiraEm">Fim da janela de validação.</param>
/// <param name="Evento">O evento desta porta.</param>
public sealed record CredencialDaPortariaDTO(Guid AcessoId, string Nome, string Token, DateTime ExpiraEm, EventoDoConviteDTO Evento);
