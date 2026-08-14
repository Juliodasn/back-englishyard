using EnglishYard.Application.Autenticacao;

namespace EnglishYard.Tests;

public sealed class AutenticacaoPerfilFotoTests
{
    [Fact]
    public void FromProfile_DevolveFotoDaProfessoraNoPerfilDaSessao()
    {
        var professoraId = Guid.NewGuid();
        const string fotoUrl = "https://example.test/professora.png";
        var profile = new PerfilUsuarioPortal(
            Guid.NewGuid(),
            "Professora 1",
            "professora1@example.com",
            PortalRoles.Professora,
            professoraId,
            fotoUrl,
            true,
            false,
            null);

        var response = PerfilUsuarioResponse.FromProfile(profile);

        Assert.Equal(professoraId, response.ProfessoraId);
        Assert.Equal(fotoUrl, response.FotoUrl);
    }
}
