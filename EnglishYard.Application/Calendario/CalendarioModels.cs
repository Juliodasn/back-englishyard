namespace EnglishYard.Application.Calendario;

public sealed record AulaCalendarioResponse(
    Guid? HorarioRecorrenteId,
    Guid? AulaId,
    Guid? ReposicaoId,
    DateOnly Data,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    Guid AlunoId,
    string AlunoNome,
    Guid ProfessoraId,
    string ProfessoraNome,
    string Status,
    string Tipo,
    bool PossuiRegistroReal,
    string? Resultado,
    bool EhReposicao);

public sealed record HorarioGradeSemanalResponse(
    Guid HorarioRecorrenteId,
    short DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    Guid AlunoId,
    string AlunoNome,
    Guid ProfessoraId,
    string ProfessoraNome);
