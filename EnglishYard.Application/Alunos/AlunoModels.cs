using EnglishYard.Domain.Entities;

namespace EnglishYard.Application.Alunos;

public sealed record HorarioRecorrenteAlunoRequest(
    short DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    Guid? ProfessoraId,
    DateOnly DataInicio);

public sealed record CadastrarAlunoRequest(
    string Nome,
    DateOnly? DataNascimento,
    string? Genero,
    string? Email,
    string? Telefone,
    string? ResponsavelNome,
    string? ResponsavelTelefone,
    string Status,
    decimal? ValorMensalidade,
    short? DiaVencimento,
    string? FormaPagamento,
    decimal TaxaMatricula,
    decimal PercentualDesconto,
    string? Observacoes,
    HorarioRecorrenteAlunoRequest[]? HorariosRecorrentes);

public sealed record AtualizarAlunoRequest(
    string Nome,
    DateOnly? DataNascimento,
    string? Genero,
    string? Email,
    string? Telefone,
    string? ResponsavelNome,
    string? ResponsavelTelefone,
    string Status,
    decimal? ValorMensalidade,
    short? DiaVencimento,
    string? FormaPagamento,
    decimal TaxaMatricula,
    decimal PercentualDesconto,
    string? Observacoes,
    DateOnly? AgendaVigenteDesde = null,
    HorarioRecorrenteAlunoRequest[]? HorariosRecorrentes = null);

public sealed record HorarioRecorrenteAlunoResponse(
    Guid Id,
    short DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    Guid ProfessoraId,
    string ProfessoraNome,
    DateOnly DataInicio,
    DateOnly? DataFim,
    bool Ativo);

public sealed record AgendaAlunoResponse(
    IReadOnlyList<HorarioRecorrenteAlunoResponse> Vigentes,
    IReadOnlyList<HorarioRecorrenteAlunoResponse> Programados,
    IReadOnlyList<HorarioRecorrenteAlunoResponse> Historico);

public sealed record ConflitoAgendaAlunoResponse(
    Guid AlunoId,
    string AlunoNome,
    Guid ProfessoraId,
    string ProfessoraNome,
    short DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFim);

public sealed record AlunoResponse(
    Guid Id,
    string Nome,
    DateOnly? DataNascimento,
    string? Genero,
    string? Email,
    string? Telefone,
    string? ResponsavelNome,
    string? ResponsavelTelefone,
    string Status,
    Guid? ProfessoraId,
    string? ProfessoraNome,
    string? ProfessoraFotoUrl,
    decimal? ValorMensalidade,
    short? DiaVencimento,
    string? FormaPagamento,
    decimal TaxaMatricula,
    decimal PercentualDesconto,
    string? Observacoes,
    string? FotoUrl,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm)
{
    public static AlunoResponse FromEntity(Aluno aluno) => new(
        aluno.Id,
        aluno.Nome,
        aluno.DataNascimento,
        aluno.Genero,
        aluno.Email,
        aluno.Telefone,
        aluno.ResponsavelNome,
        aluno.ResponsavelTelefone,
        aluno.Status,
        aluno.ProfessoraId,
        aluno.ProfessoraNome,
        aluno.ProfessoraFotoUrl,
        aluno.ValorMensalidade,
        aluno.DiaVencimento,
        aluno.FormaPagamento,
        aluno.TaxaMatricula,
        aluno.PercentualDesconto,
        aluno.Observacoes,
        aluno.FotoUrl,
        aluno.Ativo,
        aluno.CriadoEm,
        aluno.AtualizadoEm);
}

public sealed record ProfessoraResumoResponse(Guid Id, string Nome, string Status);

public sealed record AlunoListagemPaginadaResponse(
    IReadOnlyList<AlunoResponse> Itens,
    int Pagina,
    int TamanhoPagina,
    int Total,
    int TotalPaginas);

public sealed record AlunoExportacaoResponse(
    string Nome,
    string? Email,
    string? Telefone,
    string Professores,
    string DiasAula,
    decimal? ValorMensalidade,
    short? DiaVencimento,
    string Status);

