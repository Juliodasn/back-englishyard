namespace EnglishYard.Application.Operacao;

/// <summary>Single documented policy used by the portal's operational flows.</summary>
public static class PoliticaStatusOperacional
{
    public static bool ProfessoraPodeAcessar(string status) => status is "Ativa" or "Em onboarding" or "Em férias";
    public static bool ProfessoraPodeReceberAulas(string status) => status is "Ativa";
    public static bool ProfessoraPodeRegistrarHistorico(string status) => status is "Ativa" or "Em férias";
    public static bool AlunoPodeParticiparDaAgenda(string status) => status is "Ativo" or "Experimental" or "Inadimplente";
}
