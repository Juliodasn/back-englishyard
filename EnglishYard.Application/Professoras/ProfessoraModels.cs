using EnglishYard.Domain.Entities;

namespace EnglishYard.Application.Professoras;

public sealed record CadastrarProfessoraRequest(
    string Nome,
    string? NomeProfissional,
    DateOnly? DataNascimento,
    string? DocumentoIdentidade,
    string? Email,
    string SenhaInicial,
    string? Telefone,
    string Status,
    string ModeloPagamento,
    short? DiaPagamento,
    string? TipoChavePix,
    string? ChavePix,
    string? Banco,
    string? Observacoes,
    decimal ValorAulaIndividual,
    decimal ValorAulaGrupo,
    DateOnly VigenteDesde);

public sealed record AtualizarProfessoraRequest(
    string Nome,
    string? NomeProfissional,
    DateOnly? DataNascimento,
    string? DocumentoIdentidade,
    string? Telefone,
    string Status,
    string ModeloPagamento,
    short? DiaPagamento,
    string? TipoChavePix,
    string? ChavePix,
    string? Banco,
    string? Observacoes,
    decimal ValorAulaIndividual,
    decimal ValorAulaGrupo,
    DateOnly VigenteDesde);


public sealed record AtualizarMeuPerfilProfessoraRequest(
    string? NomeProfissional,
    string? Telefone);

public sealed record ProfessoraResponse(
    Guid Id,
    string Nome,
    string? NomeProfissional,
    DateOnly? DataNascimento,
    string? DocumentoIdentidade,
    string Email,
    string? Telefone,
    string Status,
    string ModeloPagamento,
    short? DiaPagamento,
    string? TipoChavePix,
    string? ChavePix,
    string? Banco,
    string? Observacoes,
    string? FotoUrl,
    bool Ativo,
    decimal ValorAulaIndividual,
    decimal ValorAulaGrupo,
    DateOnly? VigenteDesde,
    int QuantidadeAlunos,
    int QuantidadeAulas,
    DateOnly? ProximaAulaData,
    TimeOnly? ProximaAulaHora,
    bool PossuiAcesso,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm)
{
    public static ProfessoraResponse FromEntity(Professora professora) => new(
        professora.Id,
        professora.Nome,
        professora.NomeProfissional,
        professora.DataNascimento,
        professora.DocumentoIdentidade,
        professora.Email,
        professora.Telefone,
        professora.Status,
        professora.ModeloPagamento,
        professora.DiaPagamento,
        professora.TipoChavePix,
        professora.ChavePix,
        professora.Banco,
        professora.Observacoes,
        professora.FotoUrl,
        professora.Ativo,
        professora.ValorAulaIndividual,
        professora.ValorAulaGrupo,
        professora.VigenteDesde,
        professora.QuantidadeAlunos,
        professora.QuantidadeAulas,
        professora.ProximaAulaData,
        professora.ProximaAulaHora,
        professora.PossuiAcesso,
        professora.CriadoEm,
        professora.AtualizadoEm);
}


public sealed record ValorAulaProfessoraHistoricoResponse(
    Guid Id,
    decimal ValorAulaIndividual,
    decimal ValorAulaGrupo,
    DateOnly VigenteDesde,
    DateOnly? VigenteAte);

public sealed record CriarAcessoProfessoraRequest(string SenhaInicial);

public sealed record ProfessoraListagemPaginadaResponse(
    IReadOnlyList<ProfessoraResponse> Itens,
    int Pagina,
    int TamanhoPagina,
    int Total,
    int TotalPaginas);

public sealed record ProfessoraExportacaoResponse(
    string Nome,
    string Email,
    string? Telefone,
    int QuantidadeAlunos,
    int QuantidadeAulas,
    decimal ValorAulaIndividual,
    decimal ValorAulaGrupo,
    string Status);

