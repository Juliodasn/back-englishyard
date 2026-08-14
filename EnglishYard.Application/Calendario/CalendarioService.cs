namespace EnglishYard.Application.Calendario;

public sealed class CalendarioService(ICalendarioRepository repository)
{
    public Task<IReadOnlyList<AulaCalendarioResponse>> ListarAulasAsync(
        DateOnly dataInicio,
        DateOnly dataFim,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        if (dataFim < dataInicio)
            throw new CalendarioValidationException("A data final deve ser igual ou posterior à data inicial.");

        if (dataFim.DayNumber - dataInicio.DayNumber > 120)
            throw new CalendarioValidationException("Consulte no máximo 120 dias por vez.");

        return repository.ListarAulasAsync(dataInicio, dataFim, professoraId, cancellationToken);
    }

    public Task<IReadOnlyList<HorarioGradeSemanalResponse>> ListarGradeSemanalAsync(
        DateOnly dataReferencia,
        Guid? professoraId,
        CancellationToken cancellationToken)
    {
        var diasDesdeSegunda = ((int)dataReferencia.DayOfWeek + 6) % 7;
        var dataInicioSemana = dataReferencia.AddDays(-diasDesdeSegunda);
        var dataFimSemana = dataInicioSemana.AddDays(6);

        return repository.ListarGradeSemanalAsync(dataInicioSemana, dataFimSemana, professoraId, cancellationToken);
    }
}

public sealed class CalendarioValidationException(string message) : Exception(message);
