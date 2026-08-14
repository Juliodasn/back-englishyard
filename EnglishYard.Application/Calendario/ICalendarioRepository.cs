namespace EnglishYard.Application.Calendario;

public interface ICalendarioRepository
{
    Task<IReadOnlyList<AulaCalendarioResponse>> ListarAulasAsync(
        DateOnly dataInicio,
        DateOnly dataFim,
        Guid? professoraId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HorarioGradeSemanalResponse>> ListarGradeSemanalAsync(
        DateOnly dataInicioSemana,
        DateOnly dataFimSemana,
        Guid? professoraId,
        CancellationToken cancellationToken);
}
