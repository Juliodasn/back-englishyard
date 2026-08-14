namespace EnglishYard.Application.Aulas;

public interface IRegistroAulaRepository
{
    Task<IReadOnlyList<OcorrenciaAulaData>> ListarDiaAsync(
        DateOnly data,
        Guid? professoraId,
        CancellationToken cancellationToken);

    Task<OcorrenciaAulaData?> BuscarOcorrenciaAsync(
        string ocorrenciaId,
        Guid? professoraId,
        CancellationToken cancellationToken);

    Task<OcorrenciaAulaData> RegistrarResultadoAsync(
        RegistroAulaPersistenciaRequest request,
        CancellationToken cancellationToken);

    Task<OcorrenciaAulaData> AtualizarOcorrenciaAsync(
        AtualizarOcorrenciaPersistenciaRequest request,
        CancellationToken cancellationToken);

    Task<OcorrenciaAulaData> CancelarOcorrenciaAsync(
        CancelarOcorrenciaPersistenciaRequest request,
        CancellationToken cancellationToken);

    Task<OcorrenciaAulaData> AgendarAulaAvulsaAsync(
        AgendarAulaAvulsaPersistenciaRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HistoricoAulaResponse>> ListarHistoricoAsync(
        Guid aulaId,
        Guid? professoraId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReposicaoResponse>> ListarReposicoesAsync(
        Guid? professoraId,
        CancellationToken cancellationToken);

    Task<ReposicaoResponse?> BuscarReposicaoAsync(
        Guid reposicaoId,
        Guid? professoraId,
        CancellationToken cancellationToken);

    Task<ReposicaoResponse> AgendarReposicaoAsync(
        Guid reposicaoId,
        AgendarReposicaoRequest request,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken);

    Task<ReposicaoResponse> CancelarAgendamentoReposicaoAsync(
        Guid reposicaoId,
        Guid usuarioAuthId,
        string usuarioNome,
        CancellationToken cancellationToken);

}
