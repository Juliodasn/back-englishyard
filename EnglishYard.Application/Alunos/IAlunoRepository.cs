using EnglishYard.Domain.Entities;

namespace EnglishYard.Application.Alunos;

public interface IAlunoRepository
{
    Task<IReadOnlyList<Aluno>> ListarAsync(Guid? professoraId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Aluno> Itens, int Total)> ListarPaginadoAsync(
        Guid? professoraAcessoId,
        string? busca,
        Guid? professoraFiltroId,
        string? status,
        short? diaSemana,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AlunoExportacaoResponse>> ListarExportacaoAsync(
        Guid? professoraAcessoId,
        string? busca,
        Guid? professoraFiltroId,
        string? status,
        short? diaSemana,
        CancellationToken cancellationToken);
    Task<Aluno?> BuscarPorIdAsync(Guid alunoId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HorarioRecorrenteAlunoResponse>> ListarHorariosRecorrentesAsync(Guid alunoId, CancellationToken cancellationToken);
    Task<ConflitoAgendaAlunoResponse?> BuscarConflitoHorarioAsync(
        Guid? ignorarAlunoId,
        Guid professoraId,
        short diaSemana,
        TimeOnly horaInicio,
        TimeOnly horaFim,
        DateOnly dataInicio,
        CancellationToken cancellationToken);
    Task<Aluno> CadastrarAsync(CadastrarAlunoRequest request, CancellationToken cancellationToken);
    Task<Aluno?> AtualizarAsync(Guid alunoId, AtualizarAlunoRequest request, CancellationToken cancellationToken);
    Task<bool> ExcluirAsync(Guid alunoId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlunoArquivadoResponse>> ListarArquivadosAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AlunoArquivadoResponse>>([]);
    Task<bool> RestaurarAsync(Guid alunoId, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<bool> AtualizarFotoUrlAsync(Guid alunoId, string fotoUrl, CancellationToken cancellationToken);
    Task<bool> EmailExisteAsync(string email, Guid? ignorarAlunoId, CancellationToken cancellationToken);
    Task<bool> ProfessoraExisteAsync(Guid professoraId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProfessoraResumoResponse>> ListarProfessorasAtivasAsync(CancellationToken cancellationToken);
}
