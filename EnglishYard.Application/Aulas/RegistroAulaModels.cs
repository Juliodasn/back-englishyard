namespace EnglishYard.Application.Aulas;

public static class ResultadoAulaCodigos
{
    public const string Aplicada = "aplicada";
    public const string FaltaAluno = "falta_aluno";
    public const string RemarcadaAluno = "remarcada_aluno";
    public const string RemarcadaProfessora = "remarcada_professora";
    public const string Cancelada = "cancelada";

    public static readonly IReadOnlySet<string> Todos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Aplicada,
        FaltaAluno,
        RemarcadaAluno,
        RemarcadaProfessora
    };
}

public sealed record OcorrenciaAulaData(
    string OcorrenciaId,
    Guid? AulaId,
    Guid? HorarioRecorrenteId,
    Guid? ReposicaoId,
    DateOnly Data,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    Guid AlunoId,
    string AlunoNome,
    Guid ProfessoraId,
    string ProfessoraNome,
    string Tipo,
    string ParticipanteStatus,
    bool EhReposicao,
    bool PossuiRegistroReal,
    bool ElegivelPagamento,
    decimal ValorPagamento,
    string? Observacao,
    string? ReposicaoStatus);

public sealed record RegistroAulaDiaResponse(
    string OcorrenciaId,
    Guid? AulaId,
    Guid? HorarioRecorrenteId,
    Guid? ReposicaoId,
    DateOnly Data,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    Guid AlunoId,
    string AlunoNome,
    Guid ProfessoraId,
    string ProfessoraNome,
    string Tipo,
    string Status,
    string StatusCodigo,
    string SituacaoTemporal,
    bool EhReposicao,
    bool PossuiRegistroReal,
    bool ContabilizaPagamento,
    decimal ValorPagamento,
    string? Observacao,
    string? ReposicaoStatus,
    bool PodeRegistrarResultado,
    bool PodeRemarcar);

public sealed record RegistrarResultadoAulaRequest(
    string OcorrenciaId,
    string Status,
    string? Observacao);

public sealed record AtualizarOcorrenciaAulaRequest(
    string OcorrenciaId,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    string? Observacao);

public sealed record CancelarOcorrenciaAulaRequest(
    string OcorrenciaId,
    string? Motivo);

public sealed record AgendarAulaAvulsaRequest(
    Guid AlunoId,
    Guid ProfessoraId,
    DateOnly Data,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    string Tipo,
    string? Observacao,
    Guid[]? AlunoIds = null);

public sealed record HistoricoAulaResponse(
    Guid Id,
    Guid AulaId,
    Guid? AlunoId,
    string Acao,
    string? StatusAnterior,
    string? StatusNovo,
    string? Observacao,
    string AlteradoPorNome,
    DateTimeOffset AlteradoEm);

public sealed record ReposicaoResponse(
    Guid Id,
    Guid AulaOrigemId,
    Guid AlunoId,
    string AlunoNome,
    Guid ProfessoraOrigemId,
    string ProfessoraOrigemNome,
    string Motivo,
    string Status,
    DateOnly DataOrigem,
    TimeOnly HoraInicioOrigem,
    TimeOnly HoraFimOrigem,
    string? ObservacaoOrigem,
    DateOnly? DataAgendada,
    TimeOnly? HoraInicio,
    TimeOnly? HoraFim,
    Guid? ProfessoraAgendadaId,
    string? ProfessoraAgendadaNome,
    string? ObservacaoAgendamento,
    Guid? AulaReposicaoId,
    DateTimeOffset CriadoEm,
    DateTimeOffset? ConcluidaEm);

public sealed record AgendarReposicaoRequest(
    DateOnly Data,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    Guid ProfessoraId,
    string? Observacao);

public sealed record RegistroAulaPersistenciaRequest(
    OcorrenciaAulaData Ocorrencia,
    string Status,
    string? Observacao,
    Guid UsuarioAuthId,
    string UsuarioNome);

public sealed record AtualizarOcorrenciaPersistenciaRequest(
    OcorrenciaAulaData Ocorrencia,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    string? Observacao,
    Guid UsuarioAuthId,
    string UsuarioNome);

public sealed record CancelarOcorrenciaPersistenciaRequest(
    OcorrenciaAulaData Ocorrencia,
    string? Motivo,
    Guid UsuarioAuthId,
    string UsuarioNome);

public sealed record AgendarAulaAvulsaPersistenciaRequest(
    Guid AlunoId,
    Guid ProfessoraId,
    DateOnly Data,
    TimeOnly HoraInicio,
    TimeOnly HoraFim,
    string Tipo,
    string? Observacao,
    Guid UsuarioAuthId,
    string UsuarioNome,
    Guid? ProfessoraRestritaId,
    Guid[]? AlunoIds = null);

public sealed class RegistroAulaValidationException(string message) : Exception(message);
public sealed class RegistroAulaNotFoundException(string message) : Exception(message);
public sealed class RegistroAulaConflictException(string message) : Exception(message);
