namespace EnglishYard.Application.Autenticacao;

public static class PortalRoles
{
    public const string Administrador = "Administrador";
    public const string Professora = "Professora";
}

public static class PortalClaimTypes
{
    public const string ProfessoraId = "professora_id";
    public const string DeveAlterarSenha = "deve_alterar_senha";
}

public sealed record PerfilUsuarioPortal(
    Guid UsuarioAuthId,
    string Nome,
    string Email,
    string TipoUsuario,
    Guid? ProfessoraId,
    string? FotoUrl,
    bool Ativo,
    bool DeveAlterarSenha,
    DateTimeOffset? UltimoAcessoEm);

public sealed record PerfilUsuarioResponse(
    Guid UsuarioAuthId,
    string Nome,
    string Email,
    string TipoUsuario,
    Guid? ProfessoraId,
    string? FotoUrl,
    bool DeveAlterarSenha)
{
    public static PerfilUsuarioResponse FromProfile(PerfilUsuarioPortal profile) => new(
        profile.UsuarioAuthId,
        profile.Nome,
        profile.Email,
        profile.TipoUsuario,
        profile.ProfessoraId,
        profile.FotoUrl,
        profile.DeveAlterarSenha);
}

public sealed record BootstrapAdministradorRequest(
    string Nome,
    string Email,
    string SenhaInicial);

public sealed record FotoPerfilProfessoraResponse(
    Guid Id,
    string Nome,
    string? FotoUrl);


public sealed record MeuPerfilResponse(
    Guid UsuarioAuthId,
    string Nome,
    string Email,
    string TipoUsuario,
    Guid? ProfessoraId,
    string? FotoUrl,
    string? NomeProfissional,
    string? Telefone,
    string? Status,
    string? ModeloPagamento,
    short? DiaPagamento,
    string? TipoChavePix,
    string? ChavePix,
    string? Banco,
    decimal? ValorAulaIndividual,
    decimal? ValorAulaGrupo,
    DateOnly? VigenteDesde,
    bool PodeEditarDadosProfissionais);

public sealed record AtualizarMeuPerfilRequest(
    string? NomeProfissional,
    string? Telefone);

public sealed record AlterarSenhaPortalRequest(string NovaSenha);
